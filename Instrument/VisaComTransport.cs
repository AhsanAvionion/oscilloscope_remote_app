using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ScopeControl.Instrument
{
    /// <summary>
    /// Talks to the instrument through VISA-COM, the COM flavour of VISA that
    /// IO Libraries registers as "VISA.GlobalRM". It is a different API from
    /// VISA.NET but reaches the same driver underneath, so the address and the
    /// SCPI are identical.
    ///
    /// Everything here is late bound. No project reference to
    /// Ivi.Visa.Interop.dll, no interop version to keep in step, and a machine
    /// without VISA-COM fails with a readable message instead of a load error
    /// at startup.
    /// </summary>
    public sealed class VisaComTransport : IScopeTransport
    {
        private const int NoLock = 0;           // Ivi.Visa.Interop.AccessMode.NO_LOCK

        private object _resourceManager;
        private object _session;
        private int _timeout = 5000;

        public VisaComTransport(string resourceName)
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
                if (_session != null) TrySetProperty(_session, "Timeout", value);
            }
        }

        /// <summary>True when VISA-COM is registered on this machine.</summary>
        public static bool IsAvailable => Type.GetTypeFromProgID("VISA.GlobalRM") != null;

        public void Open()
        {
            Type managerType = Type.GetTypeFromProgID("VISA.GlobalRM");
            if (managerType == null)
                throw new IOException(
                    "VISA-COM is not registered on this machine (no VISA.GlobalRM class)." +
                    Environment.NewLine +
                    "Install or repair Keysight IO Libraries Suite, or use the raw socket transport.");

            try
            {
                _resourceManager = Activator.CreateInstance(managerType);
            }
            catch (Exception ex)
            {
                throw new IOException(
                    "VISA-COM is registered but could not be created: " + ex.Message +
                    Environment.NewLine +
                    "This is usually a bitness mismatch. Build as x64 to match the 64-bit VISA-COM.", ex);
            }

            try
            {
                // IResourceManager3.Open(resource, accessMode, openTimeout, optionString)
                _session = Invoke(_resourceManager, "Open",
                    ResourceName, NoLock, _timeout, string.Empty);
            }
            catch (Exception ex)
            {
                Close();
                throw new IOException(
                    "VISA-COM could not open " + ResourceName + "." + Environment.NewLine +
                    Unwrap(ex).Message + Environment.NewLine + Environment.NewLine +
                    "Check that the instrument answers a ping and that the address matches " +
                    "what Keysight Connection Expert shows.", ex);
            }

            if (_session == null)
            {
                Close();
                throw new IOException("VISA-COM returned no session for " + ResourceName + ".");
            }

            TrySetProperty(_session, "Timeout", _timeout);
            // Reads end on EOI. Termination characters must stay off, or the
            // first 0x0A inside a screenshot would cut the transfer short.
            TrySetProperty(_session, "TerminationCharacterEnabled", false);
        }

        public void Close()
        {
            if (_session != null)
            {
                try { Invoke(_session, "Close"); } catch (Exception) { }
                Release(ref _session);
            }
            Release(ref _resourceManager);
        }

        public void Write(string command)
        {
            Ensure();
            if (!command.EndsWith("\n")) command += "\n";
            Invoke(_session, "Write", Encoding.ASCII.GetBytes(command));
        }

        public string ReadString()
        {
            Ensure();
            var text = new StringBuilder();
            const int chunkSize = 4096;

            for (int guard = 0; guard < 4096; guard++)
            {
                byte[] chunk = ReadChunk(chunkSize);
                text.Append(Encoding.ASCII.GetString(chunk));

                // A short read means the instrument asserted EOI.
                if (chunk.Length < chunkSize) break;
                if (text.Length > 0 && text[text.Length - 1] == '\n') break;
            }
            return text.ToString().TrimEnd('\r', '\n');
        }

        public byte[] ReadBytes(int count)
        {
            Ensure();
            var buffer = new byte[count];
            int got = 0;
            while (got < count)
            {
                byte[] chunk = ReadChunk(count - got);
                if (chunk.Length == 0)
                    throw new IOException("VISA-COM read stopped after " + got + " of " + count + " bytes.");
                Buffer.BlockCopy(chunk, 0, buffer, got, chunk.Length);
                got += chunk.Length;
            }
            return buffer;
        }

        private byte[] ReadChunk(int count)
        {
            object result = Invoke(_session, "Read", count);
            if (result == null) return new byte[0];
            if (result is byte[] bytes) return bytes;

            // Some interop builds hand back the SAFEARRAY as object[].
            if (result is Array array)
            {
                var copy = new byte[array.Length];
                for (int i = 0; i < array.Length; i++)
                    copy[i] = Convert.ToByte(array.GetValue(i));
                return copy;
            }
            throw new IOException("VISA-COM returned an unexpected read type: " + result.GetType().Name);
        }

        public void Clear()
        {
            if (_session == null) return;
            try { Invoke(_session, "Clear"); } catch (Exception) { }
        }

        public byte[] ReadDefiniteBlock() => BlockReader.ReadIncremental(this);

        public void Dispose() => Close();

        private void Ensure()
        {
            if (_session == null) throw new InvalidOperationException("The VISA-COM session is closed.");
        }

        // ------------------------------------------------------------ late binding

        private static object Invoke(object target, string method, params object[] args)
        {
            try
            {
                return target.GetType().InvokeMember(
                    method, BindingFlags.InvokeMethod, null, target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        private static void TrySetProperty(object target, string property, object value)
        {
            try
            {
                target.GetType().InvokeMember(
                    property, BindingFlags.SetProperty, null, target, new[] { value });
            }
            catch (Exception)
            {
                // Not every interop build exposes every property; not fatal.
            }
        }

        private static void Release(ref object comObject)
        {
            if (comObject == null) return;
            try
            {
                if (Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
            }
            catch (Exception) { }
            comObject = null;
        }

        private static Exception Unwrap(Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null) ex = ex.InnerException;
            return ex;
        }
    }
}
