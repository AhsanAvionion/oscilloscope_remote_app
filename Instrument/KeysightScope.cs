using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScopeControl.Instrument
{
    public enum IoDirection { Tx, Rx, Info, Error }

    public sealed class ChannelState
    {
        public bool Display;
        public double Scale = 1.0;      // V/div
        public double Offset;           // V
        public string Coupling = "DC";
        public double Probe = 10.0;
        public bool BwLimit;
        public bool Invert;
    }

    public sealed class ScopeState
    {
        public ChannelState[] Channels = { new ChannelState(), new ChannelState(), new ChannelState(), new ChannelState() };
        public double TimeScale = 1e-3;     // s/div
        public double TimePosition;         // s, delay
        public string TimeReference = "CENT";
        public string TriggerSweep = "AUTO";
        public string TriggerMode = "EDGE";
        public string TriggerSource = "CHAN1";
        public string TriggerSlope = "POS";
        public string TriggerCoupling = "DC";
        public double TriggerLevel;
        public string AcquireType = "NORM";
        public int AcquireCount = 8;
    }

    /// <summary>
    /// SCPI wrapper for a Keysight InfiniiVision 3000 X-Series scope (MSO-X 3024G).
    /// Every public call is serialised on one gate so the UI can fire commands freely.
    /// </summary>
    public sealed class KeysightScope : IDisposable
    {
        public const int DefaultTimeoutMs = 5000;
        public const int ScreenshotTimeoutMs = 20000;

        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private IScopeTransport _transport;
        private bool _insideErrorCheck;

        public string Identity { get; private set; } = string.Empty;
        public bool AutoErrorCheck { get; set; } = true;
        public bool IsConnected => _transport != null && _transport.IsOpen;
        public string ResourceName => _transport?.ResourceName ?? string.Empty;

        /// <summary>Raised for every line sent and received. May fire on a worker thread.</summary>
        public event Action<IoDirection, string> Io;

        private bool _quiet;

        private void Log(IoDirection d, string text)
        {
            // The background state poll would otherwise bury the console under
            // thirty queries every few seconds. Errors always get through.
            if (_quiet && (d == IoDirection.Tx || d == IoDirection.Rx)) return;
            Io?.Invoke(d, text);
        }

        // ---------------------------------------------------------------- link

        public async Task<string> ConnectAsync(IScopeTransport transport)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                CloseCore();
                _transport = transport;
                await Task.Run(() =>
                {
                    _transport.TimeoutMilliseconds = DefaultTimeoutMs;
                    _transport.Open();
                    Log(IoDirection.Info, "Opened " + _transport.ResourceName);
                    Identity = QueryCore("*IDN?");
                    WriteCore("*CLS");
                    WriteCore(":SYSTem:HEADer OFF");
                }).ConfigureAwait(false);
                return Identity;
            }
            catch
            {
                CloseCore();
                throw;
            }
            finally { _gate.Release(); }
        }

        public async Task DisconnectAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { await Task.Run(CloseCore).ConfigureAwait(false); }
            finally { _gate.Release(); }
        }

        private void CloseCore()
        {
            if (_transport == null) return;
            try { _transport.Dispose(); } catch { }
            _transport = null;
            Identity = string.Empty;
            Log(IoDirection.Info, "Disconnected");
        }

        public void Dispose()
        {
            CloseCore();
            _gate.Dispose();
        }

        // ------------------------------------------------------------- plumbing

        private void Ensure()
        {
            if (_transport == null || !_transport.IsOpen)
                throw new InvalidOperationException("Not connected to an instrument.");
        }

        private void WriteCore(string command)
        {
            Ensure();
            _transport.Write(command);
            Log(IoDirection.Tx, command);
            if (AutoErrorCheck && !_insideErrorCheck) CheckErrorsCore();
        }

        private string QueryCore(string command)
        {
            Ensure();
            _transport.Write(command);
            Log(IoDirection.Tx, command);
            string answer = _transport.ReadString().Trim();
            Log(IoDirection.Rx, answer);
            return answer;
        }

        private double QueryDoubleCore(string command) => Eng.ParseScpi(QueryCore(command));

        private bool QueryBoolCore(string command)
        {
            string s = QueryCore(command);
            return s.StartsWith("1") || s.StartsWith("ON", StringComparison.OrdinalIgnoreCase);
        }

        private void CheckErrorsCore()
        {
            _insideErrorCheck = true;
            try
            {
                for (int i = 0; i < 8; i++)
                {
                    _transport.Write(":SYSTem:ERRor?");
                    string reply = _transport.ReadString().Trim();
                    if (reply.Length == 0) break;
                    if (reply.StartsWith("+0,") || reply.StartsWith("0,")) break;
                    Log(IoDirection.Error, "Instrument error: " + reply);
                }
            }
            catch (Exception ex)
            {
                Log(IoDirection.Error, "Error queue read failed: " + ex.Message);
            }
            finally { _insideErrorCheck = false; }
        }

        private async Task RunAsync(Action action)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { await Task.Run(action).ConfigureAwait(false); }
            finally { _gate.Release(); }
        }

        private async Task<T> RunAsync<T>(Func<T> func)
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { return await Task.Run(func).ConfigureAwait(false); }
            finally { _gate.Release(); }
        }

        /// <summary>Sends any SCPI line. Queries (ending in ?) return the answer.</summary>
        public Task<string> SendRawAsync(string line)
        {
            return RunAsync(() =>
            {
                string cmd = line.Trim();
                return cmd.EndsWith("?") ? QueryCore(cmd) : Pass(() => WriteCore(cmd));
            });
        }

        private static string Pass(Action a) { a(); return string.Empty; }

        // -------------------------------------------------------------- channels

        public Task SetChannelDisplayAsync(int ch, bool on) =>
            RunAsync(() => WriteCore(":CHANnel" + ch + ":DISPlay " + (on ? "ON" : "OFF")));

        public Task SetChannelScaleAsync(int ch, double voltsPerDiv) =>
            RunAsync(() => WriteCore(":CHANnel" + ch + ":SCALe " + Eng.Scpi(voltsPerDiv)));

        public Task SetChannelOffsetAsync(int ch, double volts) =>
            RunAsync(() => WriteCore(":CHANnel" + ch + ":OFFSet " + Eng.Scpi(volts)));

        public Task SetChannelCouplingAsync(int ch, string coupling) =>
            RunAsync(() => WriteCore(":CHANnel" + ch + ":COUPling " + coupling));

        public Task SetChannelProbeAsync(int ch, double attenuation) =>
            RunAsync(() => WriteCore(":CHANnel" + ch + ":PROBe " + Eng.Scpi(attenuation)));

        public Task SetChannelBwLimitAsync(int ch, bool on) =>
            RunAsync(() => WriteCore(":CHANnel" + ch + ":BWLimit " + (on ? "ON" : "OFF")));

        public Task SetChannelInvertAsync(int ch, bool on) =>
            RunAsync(() => WriteCore(":CHANnel" + ch + ":INVert " + (on ? "ON" : "OFF")));

        /// <summary>
        /// Makes a channel the selected one, so the graticule voltage labels
        /// down the left edge switch to it.
        ///
        /// There is no "select channel" command. Switching a channel's display
        /// on is what selects it, so an already-visible channel gets cycled off
        /// and straight back on. :VIEW looked like the right command but leaves
        /// the selection alone.
        /// </summary>
        public Task SelectChannelAsync(int ch, bool alreadyVisible)
        {
            return RunAsync(() =>
            {
                if (alreadyVisible) WriteCore(":CHANnel" + ch + ":DISPlay OFF");
                WriteCore(":CHANnel" + ch + ":DISPlay ON");
            });
        }

        public Task SetChannelImpedanceAsync(int ch, bool fiftyOhm) =>
            RunAsync(() => WriteCore(":CHANnel" + ch + ":IMPedance " + (fiftyOhm ? "FIFTy" : "ONEMeg")));

        // ------------------------------------------------------------ horizontal

        public Task SetTimeScaleAsync(double secondsPerDiv) =>
            RunAsync(() => WriteCore(":TIMebase:SCALe " + Eng.Scpi(secondsPerDiv)));

        public Task SetTimePositionAsync(double seconds) =>
            RunAsync(() => WriteCore(":TIMebase:POSition " + Eng.Scpi(seconds)));

        public Task SetTimeReferenceAsync(string reference) =>
            RunAsync(() => WriteCore(":TIMebase:REFerence " + reference));

        public Task SetTimeModeAsync(string mode) =>
            RunAsync(() => WriteCore(":TIMebase:MODE " + mode));

        // --------------------------------------------------------------- trigger

        /// <summary>AUTO free-runs when no trigger arrives, NORMal waits for one.</summary>
        public Task SetTriggerSweepAsync(string sweep) =>
            RunAsync(() => WriteCore(":TRIGger:SWEep " + sweep));

        public Task SetTriggerModeAsync(string mode) =>
            RunAsync(() => WriteCore(":TRIGger:MODE " + mode));

        public Task SetTriggerSourceAsync(string source) =>
            RunAsync(() => WriteCore(":TRIGger:EDGE:SOURce " + source));

        public Task SetTriggerSlopeAsync(string slope) =>
            RunAsync(() => WriteCore(":TRIGger:EDGE:SLOPe " + slope));

        public Task SetTriggerLevelAsync(double volts) =>
            RunAsync(() => WriteCore(":TRIGger:EDGE:LEVel " + Eng.Scpi(volts)));

        public Task SetTriggerCouplingAsync(string coupling) =>
            RunAsync(() => WriteCore(":TRIGger:COUPling " + coupling));

        public Task SetTriggerNoiseRejectAsync(bool on) =>
            RunAsync(() => WriteCore(":TRIGger:NREJect " + (on ? "ON" : "OFF")));

        public Task SetTriggerHfRejectAsync(bool on) =>
            RunAsync(() => WriteCore(":TRIGger:HFReject " + (on ? "ON" : "OFF")));

        /// <summary>
        /// Puts the trigger level halfway between the peaks of the trigger source,
        /// the same thing the front-panel Level knob press does.
        /// </summary>
        public Task<double> SetTriggerLevelToMidpointAsync(string source)
        {
            return RunAsync(() =>
            {
                double top = QueryDoubleCore(":MEASure:VMAX? " + source);
                double bottom = QueryDoubleCore(":MEASure:VMIN? " + source);
                if (Math.Abs(top) > 1e30 || Math.Abs(bottom) > 1e30)
                    throw new InvalidOperationException("No signal on " + source + " to measure - set the level by hand.");
                double mid = (top + bottom) / 2.0;
                WriteCore(":TRIGger:EDGE:LEVel " + Eng.Scpi(mid));
                return mid;
            });
        }

        // ----------------------------------------------------------- acquisition

        public Task SetAcquireTypeAsync(string type) =>
            RunAsync(() => WriteCore(":ACQuire:TYPE " + type));

        public Task SetAcquireCountAsync(int count) =>
            RunAsync(() => WriteCore(":ACQuire:COUNt " + count));

        public Task RunAcquisitionAsync() => RunAsync(() => WriteCore(":RUN"));
        public Task StopAcquisitionAsync() => RunAsync(() => WriteCore(":STOP"));
        public Task SingleAcquisitionAsync() => RunAsync(() => WriteCore(":SINGle"));
        public Task ClearDisplayAsync() => RunAsync(() => WriteCore(":CDISplay"));

        public Task AutoScaleAsync() =>
            RunAsync(() =>
            {
                int saved = _transport.TimeoutMilliseconds;
                _transport.TimeoutMilliseconds = 20000;
                try { WriteCore(":AUToscale"); }
                finally { _transport.TimeoutMilliseconds = saved; }
            });

        public Task DefaultSetupAsync() =>
            RunAsync(() =>
            {
                int saved = _transport.TimeoutMilliseconds;
                _transport.TimeoutMilliseconds = 20000;
                try
                {
                    WriteCore("*RST");
                    QueryCore("*OPC?");
                }
                finally { _transport.TimeoutMilliseconds = saved; }
            });

        // ---------------------------------------------------------- measurements

        public Task<double> MeasureAsync(string measurement, string source) =>
            RunAsync(() => QueryDoubleCore(":MEASure:" + measurement + "? " + source));

        // ------------------------------------------------------------ screenshot

        /// <summary>
        /// Grabs the instrument display as PNG bytes.
        /// InkSaver on gives a white background suited to printing; off matches the screen.
        /// </summary>
        public Task<byte[]> CaptureScreenAsync(bool inkSaver)
        {
            return RunAsync(() =>
            {
                int saved = _transport.TimeoutMilliseconds;
                _transport.TimeoutMilliseconds = ScreenshotTimeoutMs;
                bool savedCheck = AutoErrorCheck;
                AutoErrorCheck = false;
                try
                {
                    WriteCore(":HARDcopy:INKSaver " + (inkSaver ? "ON" : "OFF"));
                    Ensure();
                    _transport.Write(":DISPlay:DATA? PNG,COLor");
                    Log(IoDirection.Tx, ":DISPlay:DATA? PNG,COLor");
                    byte[] image = ReadDefiniteLengthBlock();
                    Log(IoDirection.Rx, "<screen image, " + image.Length + " bytes>");
                    return image;
                }
                finally
                {
                    AutoErrorCheck = savedCheck;
                    _transport.TimeoutMilliseconds = saved;
                }
            });
        }

        /// <summary>Reads an IEEE 488.2 definite-length block: #&lt;n&gt;&lt;length&gt;&lt;data&gt;.</summary>
        private byte[] ReadDefiniteLengthBlock()
        {
            byte[] head = _transport.ReadBytes(1);
            if (head[0] != (byte)'#')
                throw new IOException("Expected a block header, got byte 0x" + head[0].ToString("X2") + ".");

            int digits = _transport.ReadBytes(1)[0] - '0';
            if (digits < 1 || digits > 9)
                throw new IOException("Block header declares " + digits + " length digits.");

            string lengthText = Encoding.ASCII.GetString(_transport.ReadBytes(digits));
            if (!int.TryParse(lengthText, out int length) || length <= 0)
                throw new IOException("Block length '" + lengthText + "' is not usable.");

            byte[] data = _transport.ReadBytes(length);

            // Keysight appends a newline after the block. Swallow it so the next
            // response does not start with stale data; a timeout here is harmless.
            int saved = _transport.TimeoutMilliseconds;
            try
            {
                _transport.TimeoutMilliseconds = 800;
                _transport.ReadBytes(1);
            }
            catch { }
            finally { _transport.TimeoutMilliseconds = saved; }

            return data;
        }

        // ----------------------------------------------------------- read it all

        /// <summary>One round trip that pulls every setting the UI shows.</summary>
        public Task<ScopeState> ReadStateAsync(bool quiet = false)
        {
            return RunAsync(() =>
            {
                bool savedCheck = AutoErrorCheck;
                bool savedQuiet = _quiet;
                AutoErrorCheck = false;
                _quiet = quiet;
                try
                {
                    var state = new ScopeState();

                    for (int i = 0; i < 4; i++)
                    {
                        int ch = i + 1;
                        var c = state.Channels[i];
                        c.Display = QueryBoolCore(":CHANnel" + ch + ":DISPlay?");
                        c.Scale = QueryDoubleCore(":CHANnel" + ch + ":SCALe?");
                        c.Offset = QueryDoubleCore(":CHANnel" + ch + ":OFFSet?");
                        c.Coupling = QueryCore(":CHANnel" + ch + ":COUPling?");
                        c.Probe = QueryDoubleCore(":CHANnel" + ch + ":PROBe?");
                        c.BwLimit = QueryBoolCore(":CHANnel" + ch + ":BWLimit?");
                        c.Invert = QueryBoolCore(":CHANnel" + ch + ":INVert?");
                    }

                    state.TimeScale = QueryDoubleCore(":TIMebase:SCALe?");
                    state.TimePosition = QueryDoubleCore(":TIMebase:POSition?");
                    state.TimeReference = QueryCore(":TIMebase:REFerence?");

                    state.TriggerSweep = QueryCore(":TRIGger:SWEep?");
                    state.TriggerMode = QueryCore(":TRIGger:MODE?");
                    state.TriggerSource = QueryCore(":TRIGger:EDGE:SOURce?");
                    state.TriggerSlope = QueryCore(":TRIGger:EDGE:SLOPe?");
                    state.TriggerLevel = QueryDoubleCore(":TRIGger:EDGE:LEVel?");
                    state.TriggerCoupling = QueryCore(":TRIGger:COUPling?");

                    state.AcquireType = QueryCore(":ACQuire:TYPE?");
                    state.AcquireCount = (int)QueryDoubleCore(":ACQuire:COUNt?");

                    return state;
                }
                finally { AutoErrorCheck = savedCheck; _quiet = savedQuiet; }
            });
        }
    }
}
