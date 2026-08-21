using System;

namespace ScopeControl.Instrument
{
    /// <summary>
    /// Byte/string level link to the instrument. Two implementations exist:
    /// VisaTransport (VISA.NET, the normal path) and SocketTransport
    /// (raw TCP port 5025, useful when no VISA runtime is installed).
    /// </summary>
    public interface IScopeTransport : IDisposable
    {
        string ResourceName { get; }
        bool IsOpen { get; }
        int TimeoutMilliseconds { get; set; }

        void Open();
        void Close();

        /// <summary>Sends one SCPI line. A newline terminator is added if missing.</summary>
        void Write(string command);

        /// <summary>Reads one ASCII response (up to EOI / newline).</summary>
        string ReadString();

        /// <summary>Reads exactly <paramref name="count"/> bytes. Used for IEEE definite-length blocks.</summary>
        byte[] ReadBytes(int count);

        /// <summary>
        /// Abandons anything in flight. Needed after a query goes unanswered:
        /// a late reply would otherwise be read as the answer to the next
        /// command, and every exchange after it would be one out of step.
        /// </summary>
        void Clear();

        /// <summary>
        /// Reads one IEEE 488.2 definite-length block, leaving nothing behind.
        /// Each transport does this differently: a stream can be read piece by
        /// piece, but a USBTMC transfer is a message that must be drained fully.
        /// </summary>
        byte[] ReadDefiniteBlock();
    }
}
