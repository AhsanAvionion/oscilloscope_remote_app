using System;
using System.IO;
using System.Text;

namespace ScopeControl.Instrument
{
    /// <summary>
    /// Reads an IEEE 488.2 definite-length block: #&lt;n&gt;&lt;length&gt;&lt;data&gt;.
    ///
    /// Used by the byte-stream transports. VISA does its own, because a USBTMC
    /// transfer is a message rather than a stream and has to be drained to the
    /// end or the link is left mid-message.
    /// </summary>
    internal static class BlockReader
    {
        public static byte[] ReadIncremental(IScopeTransport transport)
        {
            byte[] head = transport.ReadBytes(1);
            if (head[0] != (byte)'#')
                throw new IOException("Expected a block header, got byte 0x" + head[0].ToString("X2") + ".");

            int digits = transport.ReadBytes(1)[0] - '0';
            if (digits < 1 || digits > 9)
                throw new IOException("Block header declares " + digits + " length digits.");

            string lengthText = Encoding.ASCII.GetString(transport.ReadBytes(digits));
            if (!int.TryParse(lengthText, out int length) || length <= 0)
                throw new IOException("Block length '" + lengthText + "' is not usable.");

            byte[] data = transport.ReadBytes(length);

            // Keysight appends a newline. Swallow it so the next response does
            // not start with a stale byte; a timeout here is harmless.
            int saved = transport.TimeoutMilliseconds;
            try
            {
                transport.TimeoutMilliseconds = 800;
                transport.ReadBytes(1);
            }
            catch (Exception) { }
            finally { transport.TimeoutMilliseconds = saved; }

            return data;
        }

        /// <summary>Pulls the payload out of a block already held in memory.</summary>
        public static byte[] Parse(byte[] message)
        {
            if (message == null || message.Length < 4)
                throw new IOException("The reply is too short to be a block.");
            if (message[0] != (byte)'#')
                throw new IOException("Expected a block header, got byte 0x" + message[0].ToString("X2") + ".");

            int digits = message[1] - '0';
            if (digits < 1 || digits > 9)
                throw new IOException("Block header declares " + digits + " length digits.");

            string lengthText = Encoding.ASCII.GetString(message, 2, digits);
            if (!int.TryParse(lengthText, out int length) || length <= 0)
                throw new IOException("Block length '" + lengthText + "' is not usable.");

            int start = 2 + digits;
            if (start + length > message.Length)
                length = message.Length - start;      // trust what arrived

            var data = new byte[length];
            Buffer.BlockCopy(message, start, data, 0, length);
            return data;
        }
    }
}
