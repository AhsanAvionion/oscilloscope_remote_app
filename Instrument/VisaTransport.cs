using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ivi.Visa;

namespace ScopeControl.Instrument
{
    /// <summary>
    /// VISA.NET message-based transport. Works with any installed VISA
    /// implementation (Keysight IO Libraries Suite, NI-VISA, ...) through
    /// Ivi.Visa.GlobalResourceManager.
    /// </summary>
    public sealed class VisaTransport : IScopeTransport
    {
        private IMessageBasedSession _session;
        private int _timeout = 5000;

        public VisaTransport(string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName))
                throw new ArgumentException("Resource name is empty.", nameof(resourceName));
            ResourceName = resourceName.Trim();
        }

        public string ResourceName { get; }

        public bool IsOpen => _session != null;

        public int TimeoutMilliseconds
        {
            get => _timeout;
            set
            {
                _timeout = value;
                if (_session != null) _session.TimeoutMilliseconds = value;
            }
        }

        public void Open()
        {
            IVisaSession raw = OpenSession();

            _session = raw as IMessageBasedSession;
            if (_session == null)
            {
                raw.Dispose();
                throw new IOException(ResourceName + " is not a message-based resource.");
            }

            _session.TimeoutMilliseconds = _timeout;
            // EOI terminates every read. Termination characters must stay off or a
            // 0x0A byte inside a screenshot would truncate the transfer.
            _session.TerminationCharacterEnabled = false;
            _session.SendEndEnabled = true;
        }

        private IVisaSession OpenSession()
        {
            Exception discoveryFailure;
            try
            {
                return GlobalResourceManager.Open(ResourceName, AccessModes.None, _timeout);
            }
            catch (Exception ex) when (LooksLikeMissingImplementation(ex))
            {
                discoveryFailure = ex;
            }
            catch (Exception ex)
            {
                throw new IOException(
                    "VISA could not open " + ResourceName + "." + Environment.NewLine + ex.Message +
                    Environment.NewLine + Environment.NewLine +
                    "Check that the instrument answers a ping, then verify the address in " +
                    "Keysight Connection Expert.", ex);
            }

            // The IVI registration is missing or was written for the other
            // bitness. Load the vendor's resource manager ourselves.
            IResourceManager vendor = VisaImplementationLoader.Find();
            if (vendor == null)
                throw new IOException(BuildNoImplementationMessage(discoveryFailure), discoveryFailure);

            try
            {
                return vendor.Open(ResourceName, AccessModes.None, _timeout);
            }
            catch (DllNotFoundException ex)
            {
                throw new IOException(
                    "The vendor VISA implementation loaded, but its native library is missing: " +
                    ex.Message + Environment.NewLine + Environment.NewLine +
                    "ktvisa32.dll is 32-bit VISA and ktvisa64.dll is 64-bit. This process is " +
                    (IntPtr.Size == 8 ? "64-bit" : "32-bit") +
                    ". Untick Prefer 32-bit in the project properties and rebuild.", ex);
            }
            catch (Exception ex)
            {
                throw new IOException(
                    "The vendor VISA implementation loaded, but could not open " + ResourceName + "." +
                    Environment.NewLine + ex.Message, ex);
            }
        }

        private static bool LooksLikeMissingImplementation(Exception ex)
        {
            string message = ex.Message ?? string.Empty;
            return message.IndexOf("implementation", StringComparison.OrdinalIgnoreCase) >= 0
                || ex is TypeInitializationException
                || ex is TypeLoadException
                || ex is FileNotFoundException;
        }

        private static string BuildNoImplementationMessage(Exception cause)
        {
            var text = new StringBuilder();
            text.AppendLine("No vendor VISA.NET implementation could be loaded.");
            text.AppendLine();
            text.AppendLine("The IVI shared components are installed, but they are only the");
            text.AppendLine("interface. The part that talks to the instrument is Keysight.Visa.dll,");
            text.AppendLine("installed by IO Libraries Suite as the optional VISA.NET component.");
            text.AppendLine();
            text.AppendLine("Two things usually fix this:");
            text.AppendLine("  1. Re-run the IO Libraries installer and enable VISA.NET support.");
            text.AppendLine("  2. Build as x64. A 32-bit process cannot use 64-bit shared components.");
            text.AppendLine();
            text.AppendLine("Meanwhile the Raw socket : 5025 transport needs no drivers at all.");
            text.AppendLine();
            text.AppendLine("Searched:");
            string[] attempts = VisaImplementationLoader.LastSearch;
            if (attempts.Length == 0)
            {
                text.AppendLine("  (nothing found to try)");
            }
            else
            {
                foreach (string line in attempts) text.AppendLine("  " + line);
            }
            text.AppendLine();
            text.AppendLine("Original error: " + cause.Message);
            return text.ToString();
        }

        public void Close()
        {
            try { _session?.Dispose(); }
            catch { /* closing a dead link is not interesting */ }
            _session = null;
        }

        public void Write(string command)
        {
            Ensure();
            if (!command.EndsWith("\n")) command += "\n";
            _session.RawIO.Write(command);
        }

        public string ReadString()
        {
            Ensure();
            return _session.RawIO.ReadString();
        }

        /// <summary>
        /// Reads the screenshot as one complete VISA message.
        ///
        /// The previous approach asked for exactly the byte count in the block
        /// header and then read the trailing newline separately. On a USBTMC
        /// link that leaves the transfer unfinished: the instrument still has
        /// the end of its message to send, the next command collides with it,
        /// and the instrument parses the wreckage as an undefined header. So
        /// read until the instrument signals the end, then parse from memory.
        /// </summary>
        public byte[] ReadDefiniteBlock()
        {
            Ensure();

            const int chunkSize = 1024 * 1024;      // one read covers any screenshot
            var message = new List<byte>();

            while (true)
            {
                byte[] chunk = _session.RawIO.Read(chunkSize);
                if (chunk == null || chunk.Length == 0) break;

                message.AddRange(chunk);

                // A short read means the instrument asserted END: the message
                // is complete and nothing is left pending on the link.
                if (chunk.Length < chunkSize) break;
            }

            return BlockReader.Parse(message.ToArray());
        }

        public void Clear()
        {
            if (_session == null) return;
            try { _session.Clear(); } catch (Exception) { }
        }

        public byte[] ReadBytes(int count)
        {
            Ensure();
            var buffer = new byte[count];
            int got = 0;
            while (got < count)
            {
                byte[] chunk = _session.RawIO.Read(count - got);
                if (chunk == null || chunk.Length == 0)
                    throw new IOException("VISA read returned no data after " + got + " of " + count + " bytes.");
                Buffer.BlockCopy(chunk, 0, buffer, got, chunk.Length);
                got += chunk.Length;
            }
            return buffer;
        }

        /// <summary>
        /// Asks VISA what is attached. This is how a USB (USBTMC) instrument is
        /// found: its address carries the vendor id, product id and serial
        /// number, so it cannot sensibly be typed from memory.
        /// </summary>
        public static string[] FindResources()
        {
            var found = new List<string>();

            foreach (string pattern in new[] { "?*INSTR", "?*SOCKET" })
            {
                try
                {
                    foreach (string name in GlobalResourceManager.Find(pattern))
                        if (!found.Contains(name)) found.Add(name);
                }
                catch (Exception)
                {
                    // Discovery not available through the shared components;
                    // fall through to the vendor implementation below.
                }
            }

            if (found.Count == 0)
            {
                IResourceManager vendor = VisaImplementationLoader.Find();
                if (vendor != null)
                {
                    foreach (string pattern in new[] { "?*INSTR", "?*SOCKET" })
                    {
                        try
                        {
                            foreach (string name in vendor.Find(pattern))
                                if (!found.Contains(name)) found.Add(name);
                        }
                        catch (Exception) { }
                    }
                }
            }

            return found.ToArray();
        }

        public void Dispose() => Close();

        private void Ensure()
        {
            if (_session == null) throw new InvalidOperationException("The VISA session is closed.");
        }
    }
}
