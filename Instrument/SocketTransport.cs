using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace ScopeControl.Instrument
{
    /// <summary>
    /// Raw SCPI socket (LAN port 5025). Same wire protocol as VISA over TCPIP,
    /// minus the VISA runtime. Handy for machines where IO Libraries is not installed.
    /// </summary>
    public sealed class SocketTransport : IScopeTransport
    {
        private readonly string _host;
        private readonly int _port;
        private TcpClient _client;
        private NetworkStream _stream;
        private int _timeout = 5000;

        public SocketTransport(string host, int port = 5025)
        {
            _host = host;
            _port = port;
            ResourceName = "TCPIP0::" + host + "::" + port + "::SOCKET";
        }

        /// <summary>Accepts a VISA-style address or a bare host name / IP.</summary>
        public static SocketTransport FromResource(string resource)
        {
            string host = resource.Trim();
            int port = 5025;

            if (host.StartsWith("TCPIP", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = host.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) throw new ArgumentException("Cannot read a host name out of " + resource);
                host = parts[1];
                if (parts.Length >= 3 && int.TryParse(parts[2], out int p)) port = p;
            }
            return new SocketTransport(host, port);
        }

        public string ResourceName { get; }

        public bool IsOpen => _client != null && _client.Connected;

        public int TimeoutMilliseconds
        {
            get => _timeout;
            set
            {
                _timeout = value;
                if (_stream != null)
                {
                    _stream.ReadTimeout = value;
                    _stream.WriteTimeout = value;
                }
            }
        }

        public void Open()
        {
            _client = new TcpClient { NoDelay = true };
            IAsyncResult ar = _client.BeginConnect(_host, _port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(_timeout))
            {
                _client.Close();
                _client = null;
                throw new IOException("No answer from " + _host + ":" + _port + " within " + _timeout + " ms.");
            }
            _client.EndConnect(ar);
            _stream = _client.GetStream();
            TimeoutMilliseconds = _timeout;
        }

        public void Close()
        {
            try { _stream?.Dispose(); } catch { }
            try { _client?.Close(); } catch { }
            _stream = null;
            _client = null;
        }

        public void Write(string command)
        {
            Ensure();
            if (!command.EndsWith("\n")) command += "\n";
            byte[] bytes = Encoding.ASCII.GetBytes(command);
            _stream.Write(bytes, 0, bytes.Length);
            _stream.Flush();
        }

        public string ReadString()
        {
            Ensure();
            var sb = new StringBuilder();
            var one = new byte[1];
            while (true)
            {
                int n = _stream.Read(one, 0, 1);
                if (n <= 0) break;
                if (one[0] == (byte)'\n') break;
                sb.Append((char)one[0]);
            }
            return sb.ToString();
        }

        public void Clear()
        {
            if (_stream == null) return;
            try
            {
                var scratch = new byte[4096];
                while (_stream.DataAvailable) _stream.Read(scratch, 0, scratch.Length);
            }
            catch (Exception) { }
        }

        public byte[] ReadBytes(int count)
        {
            Ensure();
            var buffer = new byte[count];
            int got = 0;
            while (got < count)
            {
                int n = _stream.Read(buffer, got, count - got);
                if (n <= 0) throw new IOException("Link closed after " + got + " of " + count + " bytes.");
                got += n;
            }
            return buffer;
        }

        public byte[] ReadDefiniteBlock() => BlockReader.ReadIncremental(this);

        public void Dispose() => Close();

        private void Ensure()
        {
            if (_stream == null) throw new InvalidOperationException("The socket is closed.");
        }
    }
}
