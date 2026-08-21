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

    public sealed class MathState
    {
        public bool Display;
        public string Operation = "ADD";
        public string Source1 = "CHAN1";
        public string Source2 = "CHAN2";
        public double Scale = 1.0;
        public double Offset;
        public string Window = "HANN";
        public double Center;
        public double Span;
    }

    public sealed class MarkerState
    {
        public string Mode = "OFF";
        public string X1Y1Source = "CHAN1";
        public string X2Y2Source = "CHAN1";
        public double X1, X2, Y1, Y2;
        public double XDelta, YDelta;
    }

    public sealed class ScopeState
    {
        public MathState Math = new MathState();
        public MarkerState Markers = new MarkerState();
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

        // Commands this instrument answered with "undefined header". Each one
        // costs a full timeout, so there is no sense sending it twice. Cleared
        // on connect, since the next instrument may be a different model.
        private readonly System.Collections.Generic.HashSet<string> _unsupported =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // What we last told the instrument, so settings are not re-sent on every
        // capture. Null means unknown: after connecting, or after a reset that
        // moved the instrument out from under us.
        private bool? _inkSaverState;

        // Not every model has :HARDcopy:INKSaver. Once it is rejected there is
        // no point sending it again, and leaving its error in the queue makes
        // the next unrelated failure look like something it is not.
        private bool _inkSaverSupported = true;

        /// <summary>
        /// A measurement that cannot be made never replies, so it costs a whole
        /// timeout. Keep that short: it is a readout, not a command that matters.
        /// </summary>
        public const int MeasurementTimeoutMs = 2000;

        public string Identity { get; private set; } = string.Empty;
        public bool AutoErrorCheck { get; set; } = true;

        /// <summary>
        /// Set from the model profile. Also cleared automatically if the
        /// instrument rejects the command.
        /// </summary>
        public bool InkSaverSupported
        {
            get => _inkSaverSupported;
            set { _inkSaverSupported = value; _inkSaverState = null; }
        }
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
                    _inkSaverState = null;      // new session, nothing is known
                    _unsupported.Clear();
                    Identity = QueryCore("*IDN?");
                    WriteCore("*CLS");

                    // No :SYSTem:HEADer here. That is an Infiniium command;
                    // InfiniiVision has no header mode and answers it with
                    // -113, which then sits in the queue and gets blamed on
                    // whatever fails next.
                    string leftover = ReadFirstError();
                    if (leftover.Length > 0)
                        Log(IoDirection.Error, "Error queue was not empty on connect: " + leftover);
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

        /// <summary>The header part of a command, without its arguments.</summary>
        private static string HeaderOf(string command)
        {
            string text = (command ?? string.Empty).Trim();
            int space = text.IndexOf(' ');
            return space > 0 ? text.Substring(0, space) : text;
        }

        private string QueryCore(string command)
        {
            Ensure();

            if (_unsupported.Contains(HeaderOf(command)))
                throw new NotSupportedException(HeaderOf(command) + " is not supported by this instrument.");

            _transport.Write(command);
            Log(IoDirection.Tx, command);

            string answer;
            try
            {
                answer = _transport.ReadString().Trim();
            }
            catch (Exception ex)
            {
                RecoverFromSilence(command, ex);
                throw;
            }

            Log(IoDirection.Rx, answer);
            return answer;
        }

        /// <summary>
        /// Called when a query produced no reply. Two things matter here: saying
        /// why, and leaving the link usable.
        ///
        /// An unsupported command and a busy instrument both look like silence,
        /// so the error queue is read to tell them apart - "-113 Undefined
        /// header" means the command does not exist on this model. The device
        /// clear matters just as much: without it a late reply would be read as
        /// the answer to the next command, and every exchange afterwards would
        /// be one out of step. That is why one timeout used to break everything
        /// that followed.
        /// </summary>
        private void RecoverFromSilence(string command, Exception cause)
        {
            Log(IoDirection.Error, "No reply to " + command + " (" + cause.Message + ")");

            try { _transport.Clear(); } catch (Exception) { }

            int saved = _transport.TimeoutMilliseconds;
            bool savedQuiet = _quiet;
            _quiet = false;
            try
            {
                _transport.TimeoutMilliseconds = 2000;
                _transport.Write(":SYSTem:ERRor?");
                string reply = _transport.ReadString().Trim();

                if (reply.StartsWith("+0,") || reply.StartsWith("0,"))
                {
                    Log(IoDirection.Error,
                        "The instrument reports no error, so it accepted " + command +
                        " but had no result ready. This usually means it is not " +
                        "triggering: in Normal sweep with no trigger, measurements " +
                        "never complete. Try Auto sweep or press Run.");
                }
                else
                {
                    // The queue is first-in-first-out, so this entry may belong
                    // to an earlier command rather than the one that just timed
                    // out. Report it, do not attribute it.
                    Log(IoDirection.Error, "Error queue held: " + reply +
                        "  (this may relate to an earlier command)");

                    if (reply.StartsWith("-113"))
                    {
                        string header = HeaderOf(command);
                        _unsupported.Add(header);
                        Log(IoDirection.Error,
                            header + " will not be sent again this session.");
                    }
                }
            }
            catch (Exception)
            {
                Log(IoDirection.Error, "The instrument is not responding at all. Reconnect if this repeats.");
            }
            finally
            {
                _quiet = savedQuiet;
                try { _transport.TimeoutMilliseconds = saved; } catch (Exception) { }
            }

            try { _transport.Write("*CLS"); } catch (Exception) { }
        }

        private double QueryDoubleCore(string command) => Eng.ParseScpi(QueryCore(command));

        /// <summary>Pops one entry from the error queue, or "" when clear.</summary>
        private string ReadFirstError()
        {
            bool saved = _insideErrorCheck;
            _insideErrorCheck = true;
            try
            {
                _transport.Write(":SYSTem:ERRor?");
                string reply = _transport.ReadString().Trim();
                if (reply.StartsWith("+0,") || reply.StartsWith("0,")) return string.Empty;
                return reply;
            }
            catch (Exception)
            {
                return string.Empty;
            }
            finally { _insideErrorCheck = saved; }
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

        // ------------------------------------------------------------------ math

        public Task SetMathDisplayAsync(bool on) =>
            RunAsync(() => WriteCore(":FUNCtion:DISPlay " + (on ? "ON" : "OFF")));

        public Task SetMathOperationAsync(string operation) =>
            RunAsync(() => WriteCore(":FUNCtion:OPERation " + operation));

        public Task SetMathSourceAsync(int index, string source) =>
            RunAsync(() => WriteCore(":FUNCtion:SOURce" + index + " " + source));

        public Task SetMathScaleAsync(double perDiv) =>
            RunAsync(() => WriteCore(":FUNCtion:SCALe " + Eng.Scpi(perDiv)));

        public Task SetMathOffsetAsync(double offset) =>
            RunAsync(() => WriteCore(":FUNCtion:OFFSet " + Eng.Scpi(offset)));

        public Task SetMathWindowAsync(string window) =>
            RunAsync(() => WriteCore(":FUNCtion:WINDow " + window));

        public Task SetMathCenterAsync(double hz) =>
            RunAsync(() => WriteCore(":FUNCtion:CENTer " + Eng.Scpi(hz)));

        public Task SetMathSpanAsync(double hz) =>
            RunAsync(() => WriteCore(":FUNCtion:SPAN " + Eng.Scpi(hz)));

        /// <summary>Selects the math waveform, the same display cycle the channels use.</summary>
        public Task SelectMathAsync(bool alreadyVisible)
        {
            return RunAsync(() =>
            {
                if (alreadyVisible) WriteCore(":FUNCtion:DISPlay OFF");
                WriteCore(":FUNCtion:DISPlay ON");
            });
        }

        /// <summary>True for operations that combine two sources.</summary>
        public static bool IsBinaryOperation(string operation)
        {
            string op = (operation ?? string.Empty).ToUpperInvariant();
            return op.StartsWith("ADD") || op.StartsWith("SUBT")
                || op.StartsWith("MULT") || op.StartsWith("DIV");
        }

        public static bool IsFftOperation(string operation)
        {
            return (operation ?? string.Empty).ToUpperInvariant().StartsWith("FFT");
        }

        // --------------------------------------------------------------- cursors

        /// <summary>
        /// Cursors are the MARKer subsystem. MANual gives two independent X and
        /// Y cursors; WAVeform ties Y to the waveform at each X.
        /// </summary>
        public Task SetMarkerModeAsync(string mode) =>
            RunAsync(() => WriteCore(":MARKer:MODE " + mode));

        public Task SetMarkerX1SourceAsync(string source) =>
            RunAsync(() => WriteCore(":MARKer:X1Y1source " + source));

        public Task SetMarkerX2SourceAsync(string source) =>
            RunAsync(() => WriteCore(":MARKer:X2Y2source " + source));

        public Task SetMarkerXAsync(int index, double seconds) =>
            RunAsync(() => WriteCore(":MARKer:X" + index + "Position " + Eng.Scpi(seconds)));

        public Task SetMarkerYAsync(int index, double volts) =>
            RunAsync(() => WriteCore(":MARKer:Y" + index + "Position " + Eng.Scpi(volts)));

        /// <summary>Reads both deltas in one trip, for the cursor readout.</summary>
        public Task<double[]> ReadMarkerDeltasAsync()
        {
            return RunAsync(() => new[]
            {
                QueryDoubleCore(":MARKer:XDELta?"),
                QueryDoubleCore(":MARKer:YDELta?")
            });
        }

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
                    _inkSaverState = null;      // the reset undid whatever we set
                }
                finally { _transport.TimeoutMilliseconds = saved; }
            });

        // ---------------------------------------------------------- measurements

        public Task<double> MeasureAsync(string measurement, string source) =>
            RunAsync(() => QueryDoubleCore(":MEASure:" + measurement + "? " + source));

        /// <summary>Adds a measurement to the instrument's on-screen readout.</summary>
        public Task AddMeasurementAsync(Measurement measurement, string source,
                                        string interval = "DISPlay", string type = "AC") =>
            RunAsync(() => WriteCore(measurement.AddCommand(source, interval, type)));

        /// <summary>Reads a measurement without altering what is displayed.</summary>
        public Task<double> QueryMeasurementAsync(Measurement measurement, string source,
                                                  string interval = "DISPlay", string type = "AC")
        {
            return RunAsync(() =>
            {
                int saved = _transport.TimeoutMilliseconds;
                _transport.TimeoutMilliseconds = MeasurementTimeoutMs;
                try { return QueryDoubleCore(measurement.QueryCommand(source, interval, type)); }
                finally { try { _transport.TimeoutMilliseconds = saved; } catch (Exception) { } }
            });
        }

        public Task ClearMeasurementsAsync() => RunAsync(() => WriteCore(":MEASure:CLEar"));

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
                    if (_inkSaverSupported && _inkSaverState != inkSaver)
                    {
                        // Empty the queue first. A stale entry would otherwise
                        // be read as this command's verdict, which is exactly how
                        // an unrelated error got blamed on it before.
                        while (ReadFirstError().Length > 0) { }

                        WriteCore(":HARDcopy:INKSaver " + (inkSaver ? "ON" : "OFF"));
                        string problem = ReadFirstError();
                        if (problem.StartsWith("-113"))
                        {
                            _inkSaverSupported = false;
                            Log(IoDirection.Error,
                                ":HARDcopy:INKSaver is not supported by this instrument. " +
                                "Screens will use the instrument's current setting instead.");
                        }
                        else
                        {
                            if (problem.Length > 0) Log(IoDirection.Error, problem);
                            _inkSaverState = inkSaver;
                        }
                    }
                    Ensure();
                    _transport.Write(":DISPlay:DATA? PNG,COLor");
                    Log(IoDirection.Tx, ":DISPlay:DATA? PNG,COLor");
                    byte[] image = _transport.ReadDefiniteBlock();
                    Log(IoDirection.Rx, "<screen image, " + image.Length + " bytes>");

                    // A screenshot is by far the largest transfer we do, and any
                    // residue left on the link turns the next command into
                    // gibberish that the instrument answers with -113. Clearing
                    // costs nothing and guarantees a clean start.
                    try { _transport.Clear(); } catch (Exception) { }

                    return image;
                }
                finally
                {
                    AutoErrorCheck = savedCheck;
                    _transport.TimeoutMilliseconds = saved;
                }
            });
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
                        c.Display = TryBool(":CHANnel" + ch + ":DISPlay?", c.Display);
                        c.Scale = TryDouble(":CHANnel" + ch + ":SCALe?", c.Scale);
                        c.Offset = TryDouble(":CHANnel" + ch + ":OFFSet?", c.Offset);
                        c.Coupling = TryText(":CHANnel" + ch + ":COUPling?", c.Coupling);
                        c.Probe = TryDouble(":CHANnel" + ch + ":PROBe?", c.Probe);
                        c.BwLimit = TryBool(":CHANnel" + ch + ":BWLimit?", c.BwLimit);
                        c.Invert = TryBool(":CHANnel" + ch + ":INVert?", c.Invert);
                    }

                    state.TimeScale = TryDouble(":TIMebase:SCALe?", state.TimeScale);
                    state.TimePosition = TryDouble(":TIMebase:POSition?", state.TimePosition);
                    state.TimeReference = TryText(":TIMebase:REFerence?", state.TimeReference);

                    state.TriggerSweep = TryText(":TRIGger:SWEep?", state.TriggerSweep);
                    state.TriggerMode = TryText(":TRIGger:MODE?", state.TriggerMode);
                    state.TriggerSource = TryText(":TRIGger:EDGE:SOURce?", state.TriggerSource);
                    state.TriggerSlope = TryText(":TRIGger:EDGE:SLOPe?", state.TriggerSlope);
                    state.TriggerLevel = TryDouble(":TRIGger:EDGE:LEVel?", state.TriggerLevel);
                    state.TriggerCoupling = TryText(":TRIGger:COUPling?", state.TriggerCoupling);

                    var math = state.Math;
                    math.Display = TryBool(":FUNCtion:DISPlay?", math.Display);
                    math.Operation = TryText(":FUNCtion:OPERation?", math.Operation);
                    math.Source1 = TryText(":FUNCtion:SOURce1?", math.Source1);
                    // Asking for SOURce2 on a one-source operation is a command
                    // error, and an error means no reply, which would cost a
                    // whole timeout for nothing.
                    if (IsBinaryOperation(math.Operation))
                        math.Source2 = TryText(":FUNCtion:SOURce2?", math.Source2);
                    math.Scale = TryDouble(":FUNCtion:SCALe?", math.Scale);
                    math.Offset = TryDouble(":FUNCtion:OFFSet?", math.Offset);
                    if (IsFftOperation(math.Operation))
                    {
                        math.Window = TryText(":FUNCtion:WINDow?", math.Window);
                        math.Center = TryDouble(":FUNCtion:CENTer?", math.Center);
                        math.Span = TryDouble(":FUNCtion:SPAN?", math.Span);
                    }

                    var markers = state.Markers;
                    markers.Mode = TryText(":MARKer:MODE?", markers.Mode);
                    if (!markers.Mode.ToUpperInvariant().StartsWith("OFF"))
                    {
                        markers.X1Y1Source = TryText(":MARKer:X1Y1source?", markers.X1Y1Source);
                        markers.X2Y2Source = TryText(":MARKer:X2Y2source?", markers.X2Y2Source);
                        markers.X1 = TryDouble(":MARKer:X1Position?", markers.X1);
                        markers.X2 = TryDouble(":MARKer:X2Position?", markers.X2);
                        markers.Y1 = TryDouble(":MARKer:Y1Position?", markers.Y1);
                        markers.Y2 = TryDouble(":MARKer:Y2Position?", markers.Y2);
                    }

                    state.AcquireType = TryText(":ACQuire:TYPE?", state.AcquireType);
                    state.AcquireCount = (int)TryDouble(":ACQuire:COUNt?", state.AcquireCount);

                    return state;
                }
                finally { AutoErrorCheck = savedCheck; _quiet = savedQuiet; }
            });
        }

        /// <summary>
        /// One reading that is allowed to fail. QueryCore has already flushed
        /// the link and reported the reason by the time we get here, so the
        /// remaining readings carry on with the value they had.
        /// </summary>
        private string TryText(string command, string fallback)
        {
            try { return QueryCore(command); }
            catch (Exception) { return fallback; }
        }

        private double TryDouble(string command, double fallback)
        {
            try { return Eng.ParseScpi(QueryCore(command)); }
            catch (Exception) { return fallback; }
        }

        private bool TryBool(string command, bool fallback)
        {
            try
            {
                string reply = QueryCore(command);
                return reply.StartsWith("1") || reply.StartsWith("ON", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception) { return fallback; }
        }
    }
}
