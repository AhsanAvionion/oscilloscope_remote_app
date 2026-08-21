using System;
using System.IO;
using System.IO.Ports;
using System.Text;

namespace ScopeControl.Instrument
{
    /// <summary>
    /// SCPI over a serial port. FTDI cables and other USB-to-serial adapters
    /// appear as ordinary COM ports, so this covers them.
    ///
    /// Note that the InfiniiVision 3000 X-Series has no serial port: its USB
    /// device port is USBTMC, which goes through the VISA transports with a
    /// USB0::... address. This is here for instruments that do speak SCPI over
    /// a serial line.
    ///
    /// Address forms accepted: "COM3", "COM3:9600", or VISA style "ASRL3::INSTR".
    /// </summary>
    public sealed class SerialTransport : IScopeTransport
    {
        private readonly int _baudRate;
        private SerialPort _port;
        private int _timeout = 5000;

        public SerialTransport(string portName, int baudRate = 115200)
        {
            if (string.IsNullOrWhiteSpace(portName))
                throw new ArgumentException("No serial port named.", nameof(portName));

            PortName = portName.Trim();
            _baudRate = baudRate;
            ResourceName = PortName + ":" + baudRate;
        }

        public string PortName { get; }
        public string ResourceName { get; }

        public static string[] AvailablePorts()
        {
            try { return SerialPort.GetPortNames(); }
            catch (Exception) { return new string[0]; }
        }

        /// <summary>Parses "COM3", "COM3:9600" or "ASRL3::INSTR".</summary>
        public static SerialTransport FromResource(string resource)
        {
            string text = (resource ?? string.Empty).Trim();
            int baud = 115200;

            if (text.StartsWith("ASRL", StringComparison.OrdinalIgnoreCase))
            {
                // ASRL3::INSTR -> COM3
                string digits = string.Empty;
                foreach (char c in text.Substring(4))
                {
                    if (char.IsDigit(c)) digits += c;
                    else break;
                }
                if (digits.Length == 0)
                    throw new ArgumentException("No port number in " + resource);
                return new SerialTransport("COM" + digits, baud);
            }

            int colon = text.IndexOf(':');
            if (colon > 0 && colon < text.Length - 1)
            {
                string tail = text.Substring(colon + 1);
                if (int.TryParse(tail, out int parsed)) baud = parsed;
                text = text.Substring(0, colon);
            }
            return new SerialTransport(text, baud);
        }

        public bool IsOpen => _port != null && _port.IsOpen;

        public int TimeoutMilliseconds
        {
            get => _timeout;
            set
            {
                _timeout = value;
                if (_port != null)
                {
                    _port.ReadTimeout = value;
                    _port.WriteTimeout = value;
                }
            }
        }

        public void Open()
        {
            _port = new SerialPort(PortName, _baudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                DtrEnable = true,
                RtsEnable = true,
                NewLine = "\n"
            };

            try
            {
                _port.Open();
            }
            catch (Exception ex)
            {
                _port = null;
                throw new IOException(
                    "Could not open " + PortName + " at " + _baudRate + " baud." +
                    Environment.NewLine + ex.Message + Environment.NewLine +
                    "Available ports: " + string.Join(", ", AvailablePorts()), ex);
            }

            TimeoutMilliseconds = _timeout;
            _port.DiscardInBuffer();
            _port.DiscardOutBuffer();
        }

        public void Close()
        {
            try { _port?.Close(); } catch (Exception) { }
            _port = null;
        }

        public void Write(string command)
        {
            Ensure();
            if (!command.EndsWith("\n")) command += "\n";
            _port.Write(command);
        }

        public string ReadString()
        {
            Ensure();
            var text = new StringBuilder();
            while (true)
            {
                int b = _port.ReadByte();       // throws TimeoutException on silence
                if (b < 0 || b == '\n') break;
                text.Append((char)b);
            }
            return text.ToString().TrimEnd('\r');
        }

        public void Clear()
        {
            if (_port == null || !_port.IsOpen) return;
            try { _port.DiscardInBuffer(); _port.DiscardOutBuffer(); } catch (Exception) { }
        }

        public byte[] ReadBytes(int count)
        {
            Ensure();
            var buffer = new byte[count];
            int got = 0;
            while (got < count)
            {
                int read = _port.Read(buffer, got, count - got);
                if (read <= 0) throw new IOException("Serial read stopped after " + got + " of " + count + " bytes.");
                got += read;
            }
            return buffer;
        }

        public byte[] ReadDefiniteBlock() => BlockReader.ReadIncremental(this);

        public void Dispose() => Close();

        private void Ensure()
        {
            if (_port == null || !_port.IsOpen)
                throw new InvalidOperationException("The serial port is closed.");
        }
    }
}
