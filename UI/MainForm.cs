using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ScopeControl.Instrument;

namespace ScopeControl.UI
{
    public sealed class MainForm : Form
    {
        private readonly AppSettings _settings = AppSettings.Load();
        private readonly KeysightScope _scope = new KeysightScope();
        private readonly ChannelPanel[] _channels = new ChannelPanel[4];
        private readonly Timer _refreshTimer = new Timer();
        private readonly Timer _stateTimer = new Timer();

        private bool _suspend;          // true while loading state back from the instrument
        private bool _capturing;
        private bool _syncingState;
        private DateTime _lastUserCommand = DateTime.MinValue;
        private int _captureFailures;
        private byte[] _lastPng;

        // connection bar
        private ComboBox _address;
        private ComboBox _transport;
        private Button _connect;
        private Button _disconnect;
        private Label _identity;

        // screen
        private ScopeScreen _screen;
        private ComboBox _measureChannel;
        private Label _vpp;
        private Label _frequency;
        private Label _acqState;

        // horizontal
        private ComboBox _timeScale;
        private EngBox _delay;
        private ComboBox _timeReference;

        // trigger
        private Button _sweepAuto;
        private Button _sweepNormal;
        private ComboBox _triggerType;
        private ComboBox _triggerSource;
        private Button[] _slopeKeys;
        private EngBox _triggerLevel;
        private Button _levelMidpoint;
        private ComboBox _triggerCoupling;
        private CheckBox _noiseReject;
        private CheckBox _hfReject;
        private static readonly string[] SlopeScpi = { "POSitive", "NEGative", "EITHer", "ALTernate" };

        // acquisition
        private ComboBox _acquireType;
        private ComboBox _averageCount;

        // display / capture
        private Button _capture;
        private Button _savePng;
        private CheckBox _autoRefresh;
        private ComboBox _refreshInterval;
        private CheckBox _inkSaver;
        private CheckBox _errorCheck;
        private CheckBox _followPanel;
        private ComboBox _syncInterval;

        // console
        private Panel _console;
        private Button _consoleToggle;
        private Panel _topBar;
        private Button _topStrip;
        private RichTextBox _log;
        private TextBox _command;

        private Panel[] _groups;

        public MainForm()
        {
            Text = "InfiniiVision MSO-X 3024G — Remote Control";
            // Every size in this file is a pixel value picked at 96 DPI. Without
            // this, a 125% or 150% display renders the fonts larger while the
            // controls stay put, and the text gets clipped.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            MinimumSize = new Size(1200, 820);
            ClientSize = new Size(
                _settings.WindowWidth > 0 ? _settings.WindowWidth : 1440,
                _settings.WindowHeight > 0 ? _settings.WindowHeight : 940);
            BackColor = Theme.Chassis;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            BuildUi();
            ShowConsole(_settings.ShowConsole);
            ShowTopBar(_settings.ShowTopBar);
            if (_settings.Maximized) WindowState = FormWindowState.Maximized;

            _scope.Io += OnIo;
            AppendLog(IoDirection.Info,
                "ScopeControl started, " + (IntPtr.Size == 8 ? "64-bit" : "32-bit") +
                " process. VISA needs a matching native library.");
            _refreshTimer.Tick += async (s, e) => await CaptureScreenAsync(silent: true);
            _stateTimer.Tick += async (s, e) => await SyncStateAsync();
            SetConnectedUi(false);
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // Connect straight away with the default address and transport.
            // A failure here only logs; the user can change either and retry.
            await ConnectAsync(showErrors: false);
        }

        // ============================================================== layout

        private void BuildUi()
        {
            // Docking is resolved from the highest z-index down, so the fill
            // control goes in first and the outermost edge goes in last.
            Controls.Add(BuildCenter());
            Controls.Add(BuildSidePanel());
            Controls.Add(BuildConsole());
            Controls.Add(BuildTopBar());
            Controls.Add(BuildTopStrip());
        }

        /// <summary>
        /// A thin strip that is always there, showing what we are connected to.
        /// Clicking it folds the connection controls in and out, so the display
        /// gets the height back when they are not needed.
        /// </summary>
        private Control BuildTopStrip()
        {
            _topStrip = new Button
            {
                Dock = DockStyle.Top,
                Height = 20,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Bezel,
                ForeColor = Theme.TextDim,
                Font = Theme.Ui,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0),
                TabStop = false
            };
            _topStrip.FlatAppearance.BorderSize = 0;
            _topStrip.FlatAppearance.MouseOverBackColor = Theme.Key;
            _topStrip.Click += (s, e) => ShowTopBar(!_topBar.Visible);
            return _topStrip;
        }

        private Control BuildTopBar()
        {
            // Width matters before the children go in: WinForms works out anchor
            // offsets against the parent's size at that moment, and a docked
            // panel is still 200 wide until it is added to the form. Anything
            // anchored right would end up off-screen once it stretched.
            _topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                Width = ClientSize.Width,
                BackColor = Theme.Bezel
            };

            _address = new ComboBox
            {
                Location = new Point(8, 5),
                Size = new Size(250, 22),
                DropDownStyle = ComboBoxStyle.DropDown,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Field,
                ForeColor = Theme.Text,
                Font = Theme.Mono
            };
            _address.Items.AddRange(new object[]
            {
                "TCPIP0::10.10.0.222::INSTR",
                "TCPIP0::10.10.0.222::5025::SOCKET",
                "TCPIP0::10.10.0.222::hislip0::INSTR"
            });
            foreach (string recent in _settings.RecentAddresses)
                if (!_address.Items.Contains(recent)) _address.Items.Insert(0, recent);
            if (!_address.Items.Contains(_settings.Address))
                _address.Items.Insert(0, _settings.Address);
            _address.Text = _settings.Address;

            _transport = UiFactory.Combo(264, 5, 132);
            UiFactory.FillCombo(_transport,
                new Choice("Raw socket : 5025", "SOCKET"),
                new Choice("VISA.NET", "VISA"),
                new Choice("VISA-COM", "VISACOM"));
            SelectTransport(_settings.Transport);

            _connect = UiFactory.Key("Connect", 402, 5, 80, 22);
            _connect.Font = Theme.Ui;
            _connect.BackColor = Theme.Run;
            _connect.ForeColor = Color.White;
            _connect.Click += async (s, e) => await ConnectAsync(showErrors: true);

            _disconnect = UiFactory.Key("Disconnect", 488, 5, 84, 22);
            _disconnect.Font = Theme.Ui;
            _disconnect.Click += async (s, e) => await DisconnectAsync();

            _consoleToggle = UiFactory.Key("Show console", 578, 5, 110, 22);
            _consoleToggle.Font = Theme.Ui;
            _consoleToggle.Click += (s, e) => ShowConsole(!_console.Visible);

            _identity = new Label
            {
                Text = "No instrument connected",
                Location = new Point(698, 8),
                Size = new Size(Math.Max(120, ClientSize.Width - 710), 16),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Theme.TextDim,
                Font = Theme.Mono,
                AutoEllipsis = true
            };

            _topBar.Controls.AddRange(new Control[]
            {
                _address, _transport, _connect, _disconnect, _consoleToggle, _identity
            });
            return _topBar;
        }

        private void ShowTopBar(bool visible)
        {
            _topBar.Visible = visible;
            UpdateTopStrip();
        }

        /// <summary>Keeps the strip caption in step with the connection.</summary>
        private void UpdateTopStrip()
        {
            if (_topStrip == null) return;

            string chevron = _topBar != null && _topBar.Visible ? "\u25B4" : "\u25BE";
            string state = _scope.IsConnected
                ? "connected to " + _scope.ResourceName
                : "not connected";
            _topStrip.Text = chevron + "   KEYSIGHT InfiniiVision MSO-X 3024G  \u00B7  " + state;
            _topStrip.ForeColor = _scope.IsConnected ? Theme.Accent : Theme.TextDim;
        }

        /// <summary>Exact match only: the prefix rule used elsewhere would pick
        /// VISA.NET when VISACOM was asked for.</summary>
        private void SelectTransport(string scpi)
        {
            for (int i = 0; i < _transport.Items.Count; i++)
            {
                if (string.Equals(((Choice)_transport.Items[i]).Scpi, scpi,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _transport.SelectedIndex = i;
                    return;
                }
            }
        }

        private Control BuildCenter()
        {
            var center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Chassis };

            // --- channel strip along the bottom, one column per channel
            var strip = new TableLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 150,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = Theme.Chassis,
                Margin = new Padding(0),
                Padding = new Padding(4, 0, 4, 4)
            };
            for (int i = 0; i < 4; i++)
                strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            for (int i = 0; i < 4; i++)
            {
                var panel = new ChannelPanel(i + 1) { Dock = DockStyle.Fill, Margin = new Padding(4) };
                WireChannel(panel);
                _channels[i] = panel;
                strip.Controls.Add(panel, i, 0);
            }

            // --- screen with bezel
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Bezel,
                Padding = new Padding(10, 10, 10, 4),
                Margin = new Padding(0)
            };

            var readouts = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Theme.Bezel };

            _measureChannel = UiFactory.Combo(4, 4, 76);
            for (int i = 1; i <= 4; i++) _measureChannel.Items.Add(new Choice("CH " + i, "CHANnel" + i));
            _measureChannel.SelectedIndex = 0;
            _measureChannel.SelectedIndexChanged += async (s, e) => await UpdateMeasurementsAsync();

            _vpp = UiFactory.Value("Vpp  ----", 90, 5, 160, Theme.Text);
            _frequency = UiFactory.Value("Freq ----", 256, 5, 190, Theme.Text);

            _acqState = new Label
            {
                Text = "IDLE",
                Dock = DockStyle.Right,
                Width = 220,
                Padding = new Padding(0, 0, 6, 0),
                ForeColor = Theme.TextDim,
                Font = Theme.Readout,
                TextAlign = ContentAlignment.MiddleRight
            };

            readouts.Controls.AddRange(new Control[] { _measureChannel, _vpp, _frequency, _acqState });

            _screen = new ScopeScreen { Dock = DockStyle.Fill };

            host.Controls.Add(_screen);
            host.Controls.Add(readouts);

            center.Controls.Add(host);
            center.Controls.Add(strip);
            return center;
        }

        private Control BuildSidePanel()
        {
            var side = new Panel
            {
                Dock = DockStyle.Right,
                Width = 372,
                BackColor = Theme.Chassis,
                AutoScroll = true,
                Padding = new Padding(8, 8, 8, 8)
            };

            int width = 348;
            int y = 8;

            var horizontal = BuildHorizontalGroup(8, y, width); y += horizontal.Height + 10;
            var trigger = BuildTriggerGroup(8, y, width); y += trigger.Height + 10;
            var acquire = BuildAcquireGroup(8, y, width); y += acquire.Height + 10;
            var run = BuildRunGroup(8, y, width); y += run.Height + 10;
            var display = BuildDisplayGroup(8, y, width);

            _groups = new[] { horizontal, trigger, acquire, run, display };
            side.Controls.AddRange(_groups);
            return side;
        }

        private Panel BuildHorizontalGroup(int x, int y, int width)
        {
            var g = UiFactory.Group("HORIZONTAL", x, y, width, 118);

            g.Controls.Add(UiFactory.Caption("Time/div", 10, 32, 66));
            _timeScale = UiFactory.Combo(80, 32, 170);
            foreach (double v in Eng.Sequence125(1e-9, 50))
                _timeScale.Items.Add(new Choice(Eng.Format(v, "s"), v));
            UiFactory.SelectValue(_timeScale, 1e-3);
            _timeScale.SelectedIndexChanged += (s, e) =>
            {
                if (_suspend || _timeScale.SelectedItem == null) return;
                double v = ((Choice)_timeScale.SelectedItem).Value;
                _delay.Step = v;
                Do(() => _scope.SetTimeScaleAsync(v));
            };

            g.Controls.Add(UiFactory.Caption("Delay", 10, 58, 66));
            _delay = new EngBox
            {
                Location = new Point(80, 58),
                Size = new Size(250, 22),
                Unit = "s",
                Step = 1e-3,
                Minimum = -500,
                Maximum = 500
            };
            _delay.ValueCommitted += (s, e) => { if (!_suspend) Do(() => _scope.SetTimePositionAsync(_delay.Value)); };

            g.Controls.Add(UiFactory.Caption("Reference", 10, 84, 66));
            _timeReference = UiFactory.Combo(80, 84, 170);
            UiFactory.FillCombo(_timeReference,
                new Choice("Left", "LEFT"), new Choice("Center", "CENTer"), new Choice("Right", "RIGHt"));
            _timeReference.SelectedIndex = 1;
            _timeReference.SelectedIndexChanged += (s, e) =>
            {
                if (_suspend || _timeReference.SelectedItem == null) return;
                Do(() => _scope.SetTimeReferenceAsync(((Choice)_timeReference.SelectedItem).Scpi));
            };

            g.Controls.AddRange(new Control[] { _timeScale, _delay, _timeReference });
            return g;
        }

        private Panel BuildTriggerGroup(int x, int y, int width)
        {
            var g = UiFactory.Group("TRIGGER", x, y, width, 224);

            g.Controls.Add(UiFactory.Caption("Sweep", 10, 30, 66));
            _sweepAuto = UiFactory.Key("Auto", 80, 30, 82, 24);
            _sweepNormal = UiFactory.Key("Normal", 166, 30, 82, 24);
            _sweepAuto.Click += (s, e) => { SetSweepUi("AUTO"); Do(() => _scope.SetTriggerSweepAsync("AUTO")); };
            _sweepNormal.Click += (s, e) => { SetSweepUi("NORM"); Do(() => _scope.SetTriggerSweepAsync("NORMal")); };
            var sweepHint = new Label
            {
                Text = "Normal waits for an edge",
                Location = new Point(254, 28),
                Size = new Size(88, 28),
                ForeColor = Theme.TextDim,
                Font = new Font("Segoe UI", 6.75f)
            };

            g.Controls.Add(UiFactory.Caption("Type", 10, 60, 66));
            _triggerType = UiFactory.Combo(80, 60, 170);
            UiFactory.FillCombo(_triggerType,
                new Choice("Edge", "EDGE"),
                new Choice("Pulse width", "GLITch"),
                new Choice("Pattern", "PATTern"));
            _triggerType.SelectedIndexChanged += (s, e) =>
            {
                if (_triggerType.SelectedItem == null) return;
                string mode = ((Choice)_triggerType.SelectedItem).Scpi;
                UpdateEdgeControlsEnabled(mode);
                if (_suspend) return;
                Do(() => _scope.SetTriggerModeAsync(mode));
            };

            g.Controls.Add(UiFactory.Caption("Source", 10, 86, 66));
            _triggerSource = UiFactory.Combo(80, 86, 170);
            UiFactory.FillCombo(_triggerSource,
                new Choice("CH 1", "CHANnel1"), new Choice("CH 2", "CHANnel2"),
                new Choice("CH 3", "CHANnel3"), new Choice("CH 4", "CHANnel4"),
                new Choice("External", "EXTernal"), new Choice("Line", "LINE"),
                new Choice("Wave gen", "WGEN"));
            _triggerSource.SelectedIndexChanged += (s, e) =>
            {
                if (_suspend || _triggerSource.SelectedItem == null) return;
                Do(() => _scope.SetTriggerSourceAsync(((Choice)_triggerSource.SelectedItem).Scpi));
            };

            g.Controls.Add(UiFactory.Caption("Slope", 10, 114, 66));
            string[] labels = { "\u2191 Rising", "\u2193 Falling", "Either", "Alternate" };
            _slopeKeys = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var key = UiFactory.Key(labels[i], 80 + i * 64, 112, 61, 24);
                key.Font = new Font("Segoe UI Semibold", 7.25f, FontStyle.Bold);
                key.Click += (s, e) =>
                {
                    SetSlopeUi(index);
                    Do(() => _scope.SetTriggerSlopeAsync(SlopeScpi[index]));
                };
                _slopeKeys[i] = key;
                g.Controls.Add(key);
            }

            g.Controls.Add(UiFactory.Caption("Level", 10, 144, 66));
            _triggerLevel = new EngBox
            {
                Location = new Point(80, 144),
                Size = new Size(178, 22),
                Unit = "V",
                Step = 0.1,
                Minimum = -100,
                Maximum = 100
            };
            _triggerLevel.ValueCommitted += (s, e) =>
            {
                if (!_suspend) Do(() => _scope.SetTriggerLevelAsync(_triggerLevel.Value));
            };
            _levelMidpoint = UiFactory.Key("Set 50%", 262, 144, 80, 22);
            _levelMidpoint.Font = Theme.Ui;
            _levelMidpoint.Click += (s, e) => Do(async () =>
            {
                string source = ((Choice)_triggerSource.SelectedItem).Scpi;
                double level = await _scope.SetTriggerLevelToMidpointAsync(source);
                _triggerLevel.SetValueSilently(level);
            });

            g.Controls.Add(UiFactory.Caption("Coupling", 10, 172, 66));
            _triggerCoupling = UiFactory.Combo(80, 172, 100);
            UiFactory.FillCombo(_triggerCoupling,
                new Choice("DC", "DC"), new Choice("AC", "AC"), new Choice("LF reject", "LFReject"));
            _triggerCoupling.SelectedIndexChanged += (s, e) =>
            {
                if (_suspend || _triggerCoupling.SelectedItem == null) return;
                Do(() => _scope.SetTriggerCouplingAsync(((Choice)_triggerCoupling.SelectedItem).Scpi));
            };

            _noiseReject = UiFactory.Check("Noise reject", 190, 172, 100);
            _noiseReject.CheckedChanged += (s, e) =>
            {
                if (!_suspend) Do(() => _scope.SetTriggerNoiseRejectAsync(_noiseReject.Checked));
            };
            _hfReject = UiFactory.Check("HF reject", 80, 194, 100);
            _hfReject.CheckedChanged += (s, e) =>
            {
                if (!_suspend) Do(() => _scope.SetTriggerHfRejectAsync(_hfReject.Checked));
            };

            g.Controls.AddRange(new Control[]
            {
                _sweepAuto, _sweepNormal, sweepHint, _triggerType, _triggerSource,
                _triggerLevel, _levelMidpoint, _triggerCoupling, _noiseReject, _hfReject
            });

            SetSweepUi("AUTO");
            SetSlopeUi(0);
            return g;
        }

        private Panel BuildAcquireGroup(int x, int y, int width)
        {
            var g = UiFactory.Group("ACQUIRE", x, y, width, 92);

            g.Controls.Add(UiFactory.Caption("Mode", 10, 32, 66));
            _acquireType = UiFactory.Combo(80, 32, 170);
            UiFactory.FillCombo(_acquireType,
                new Choice("Normal", "NORMal"), new Choice("Averaging", "AVERage"),
                new Choice("High resolution", "HRESolution"), new Choice("Peak detect", "PEAK"));
            _acquireType.SelectedIndexChanged += (s, e) =>
            {
                if (_acquireType.SelectedItem == null) return;
                string type = ((Choice)_acquireType.SelectedItem).Scpi;
                _averageCount.Enabled = type == "AVERage";
                if (_suspend) return;
                Do(() => _scope.SetAcquireTypeAsync(type));
            };

            g.Controls.Add(UiFactory.Caption("Averages", 10, 58, 66));
            _averageCount = UiFactory.Combo(80, 58, 100);
            foreach (int n in new[] { 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024, 8192, 65536 })
                _averageCount.Items.Add(new Choice(n.ToString(), n));
            _averageCount.SelectedIndex = 2;
            _averageCount.Enabled = false;
            _averageCount.SelectedIndexChanged += (s, e) =>
            {
                if (_suspend || _averageCount.SelectedItem == null) return;
                Do(() => _scope.SetAcquireCountAsync((int)((Choice)_averageCount.SelectedItem).Value));
            };

            g.Controls.AddRange(new Control[] { _acquireType, _averageCount });
            return g;
        }

        private Panel BuildRunGroup(int x, int y, int width)
        {
            var g = UiFactory.Group("RUN CONTROL", x, y, width, 112);

            var run = UiFactory.Key("Run", 10, 32, 105, 32);
            run.BackColor = Theme.Run;
            run.ForeColor = Color.White;
            run.Click += (s, e) => Do(async () => { await _scope.RunAcquisitionAsync(); SetAcqState("RUNNING", Theme.Run); });

            var stop = UiFactory.Key("Stop", 121, 32, 105, 32);
            stop.BackColor = Theme.Stop;
            stop.ForeColor = Color.White;
            stop.Click += (s, e) => Do(async () => { await _scope.StopAcquisitionAsync(); SetAcqState("STOPPED", Theme.Stop); });

            var single = UiFactory.Key("Single", 232, 32, 106, 32);
            single.BackColor = Theme.Single;
            single.ForeColor = UiFactory.PickReadableText(Theme.Single);
            single.Click += (s, e) => Do(async () => { await _scope.SingleAcquisitionAsync(); SetAcqState("SINGLE", Theme.Single); });

            var autoScale = UiFactory.Key("Auto scale", 10, 70, 105, 28);
            autoScale.Click += (s, e) => Do(async () =>
            {
                SetAcqState("AUTO SCALING", Theme.Accent);
                await _scope.AutoScaleAsync();
                await ReloadStateAsync();
                await CaptureScreenAsync(silent: true);
                SetAcqState("RUNNING", Theme.Run);
            });

            var defaults = UiFactory.Key("Default setup", 121, 70, 105, 28);
            defaults.Font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold);
            defaults.Click += (s, e) =>
            {
                var answer = MessageBox.Show(this,
                    "Default setup resets every instrument setting to the factory state.\r\nContinue?",
                    "Default setup", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                if (answer != DialogResult.OK) return;
                Do(async () => { await _scope.DefaultSetupAsync(); await ReloadStateAsync(); });
            };

            var clear = UiFactory.Key("Clear display", 232, 70, 106, 28);
            clear.Font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold);
            clear.Click += (s, e) => Do(() => _scope.ClearDisplayAsync());

            g.Controls.AddRange(new Control[] { run, stop, single, autoScale, defaults, clear });
            return g;
        }

        private Panel BuildDisplayGroup(int x, int y, int width)
        {
            var g = UiFactory.Group("SCREEN", x, y, width, 152);

            _capture = UiFactory.Key("Capture screen", 10, 32, 160, 28);
            _capture.BackColor = Theme.Accent;
            _capture.ForeColor = UiFactory.PickReadableText(Theme.Accent);
            _capture.Click += async (s, e) => await CaptureScreenAsync(silent: false);

            _savePng = UiFactory.Key("Save as PNG", 176, 32, 162, 28);
            _savePng.Click += (s, e) => SaveScreenshot();

            _autoRefresh = UiFactory.Check("Refresh automatically", 10, 66, 148);

            _refreshInterval = UiFactory.Combo(164, 64, 90);
            foreach (var choice in new[]
            {
                new Choice("0.5 s", 500.0), new Choice("1 s", 1000.0),
                new Choice("2 s", 2000.0), new Choice("5 s", 5000.0)
            }) _refreshInterval.Items.Add(choice);
            UiFactory.SelectValue(_refreshInterval, _settings.RefreshIntervalMs);

            // Wire the handlers only once both controls exist, then apply the
            // saved state: ApplyRefreshTimer reads the interval combo.
            _autoRefresh.CheckedChanged += (s, e) => ApplyRefreshTimer();
            _refreshInterval.SelectedIndexChanged += (s, e) => ApplyRefreshTimer();
            _autoRefresh.Checked = _settings.AutoRefresh;

            _inkSaver = UiFactory.Check("Ink saver (white background)", 10, 92, 190);
            _inkSaver.Checked = _settings.InkSaver;
            _errorCheck = UiFactory.Check("Read error queue", 206, 92, 132);
            _errorCheck.Checked = _settings.CheckErrors;
            _errorCheck.CheckedChanged += (s, e) => _scope.AutoErrorCheck = _errorCheck.Checked;

            // Someone may be turning knobs on the instrument itself. Re-read the
            // settings periodically so the panel follows what they do there.
            _followPanel = UiFactory.Check("Follow front panel every", 10, 118, 150);
            _syncInterval = UiFactory.Combo(164, 116, 90);
            foreach (var choice in new[]
            {
                new Choice("2 s", 2000.0), new Choice("5 s", 5000.0),
                new Choice("10 s", 10000.0), new Choice("30 s", 30000.0)
            }) _syncInterval.Items.Add(choice);
            UiFactory.SelectValue(_syncInterval, _settings.SyncIntervalMs);
            if (_syncInterval.SelectedIndex < 0) _syncInterval.SelectedIndex = 1;

            _followPanel.CheckedChanged += (s, e) => ApplySyncTimer();
            _syncInterval.SelectedIndexChanged += (s, e) => ApplySyncTimer();
            _followPanel.Checked = _settings.FollowPanel;

            g.Controls.AddRange(new Control[]
            {
                _capture, _savePng, _autoRefresh, _refreshInterval, _inkSaver, _errorCheck,
                _followPanel, _syncInterval
            });
            return g;
        }

        private Control BuildConsole()
        {
            var console = new Panel { Dock = DockStyle.Bottom, Height = 168, BackColor = Theme.Chassis, Padding = new Padding(8, 4, 8, 8) };

            var entry = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = Theme.Chassis,
                Padding = new Padding(0, 4, 0, 4)
            };

            var caption = UiFactory.Caption("SCPI", 0, 0, 40);
            caption.Dock = DockStyle.Left;

            _command = UiFactory.Field(0, 0, 100);
            _command.Dock = DockStyle.Fill;
            _command.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                SendRawCommand();
            };

            var send = UiFactory.Key("Send", 0, 0, 80, 22);
            send.Dock = DockStyle.Right;
            send.Click += (s, e) => SendRawCommand();

            var clear = UiFactory.Key("Clear log", 0, 0, 88, 22);
            clear.Dock = DockStyle.Right;
            clear.Click += (s, e) => _log.Clear();

            entry.Controls.Add(_command);
            entry.Controls.Add(send);
            entry.Controls.Add(clear);
            entry.Controls.Add(caption);

            _log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Field,
                ForeColor = Theme.TextDim,
                Font = Theme.Mono,
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true,
                WordWrap = false,
                DetectUrls = false
            };

            console.Controls.Add(_log);
            console.Controls.Add(entry);

            // Hidden by default; the top-bar key and F12 bring it back.
            console.Visible = false;
            _console = console;
            return console;
        }

        /// <summary>
        /// Makes a channel the selected one, which is what moves the graticule
        /// voltage labels down the left edge of the instrument display.
        /// </summary>
        private void SelectChannel(int channel)
        {
            ChannelPanel target = null;
            foreach (var panel in _channels)
            {
                if (panel == null) continue;
                panel.IsSelected = panel.Number == channel;
                if (panel.Number == channel) target = panel;
            }
            if (target == null) return;

            bool visible = target.IsOn;
            target.IsOn = true;              // selecting always leaves it on

            Do(async () =>
            {
                await _scope.SelectChannelAsync(channel, visible);
                await CaptureScreenAsync(silent: true);
            });
        }

        private void ShowConsole(bool visible)
        {
            _console.Visible = visible;
            _consoleToggle.Text = visible ? "Hide console" : "Show console";
            if (visible)
            {
                ScrollLogToEnd();
                if (_command.IsHandleCreated) _command.Focus();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F12)
            {
                ShowConsole(!_console.Visible);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        private void WireChannel(ChannelPanel panel)
        {
            panel.SelectRequested += SelectChannel;
            panel.EnabledChanged += (ch, on) => Do(() => _scope.SetChannelDisplayAsync(ch, on));
            panel.ScaleChanged += (ch, v) => Do(() => _scope.SetChannelScaleAsync(ch, v));
            panel.OffsetChanged += (ch, v) => Do(() => _scope.SetChannelOffsetAsync(ch, v));
            panel.CouplingChanged += (ch, v) => Do(() => _scope.SetChannelCouplingAsync(ch, v));
            panel.ProbeChanged += (ch, v) => Do(() => _scope.SetChannelProbeAsync(ch, v));
            panel.BwLimitChanged += (ch, v) => Do(() => _scope.SetChannelBwLimitAsync(ch, v));
            panel.InvertChanged += (ch, v) => Do(() => _scope.SetChannelInvertAsync(ch, v));
        }

        // ========================================================== connection

        private async Task ConnectAsync(bool showErrors)
        {
            string resource = _address.Text.Trim();
            if (resource.Length == 0)
            {
                AppendLog(IoDirection.Error, "Enter a VISA address, for example TCPIP0::10.10.0.222::INSTR");
                return;
            }

            string mode = ((Choice)_transport.SelectedItem).Scpi;
            _connect.Enabled = false;
            _screen.Message = "Opening " + resource + " ...";

            try
            {
                IScopeTransport transport;
                switch (mode)
                {
                    case "SOCKET":
                        transport = SocketTransport.FromResource(resource);
                        break;
                    case "VISACOM":
                        transport = new VisaComTransport(resource);
                        break;
                    default:
                        transport = new VisaTransport(resource);
                        break;
                }

                _scope.AutoErrorCheck = _errorCheck.Checked;
                string id = await _scope.ConnectAsync(transport);

                _identity.Text = id;
                _identity.ForeColor = Theme.Text;
                UpdateTopStrip();

                // Save now rather than at shutdown, so a working address
                // survives even if the app is killed.
                _settings.Address = resource;
                _settings.Transport = mode;
                _settings.Save();
                SetConnectedUi(true);
                _screen.Message = "Connected. Press Capture screen.";

                await ReloadStateAsync();
                await CaptureScreenAsync(silent: true);
                ApplyRefreshTimer();
                ApplySyncTimer();
            }
            catch (Exception ex)
            {
                AppendLog(IoDirection.Error, ex.Message);
                _screen.Message = "Could not open " + resource + ". Pick a transport and press Connect.";
                SetConnectedUi(false);
                if (showErrors)
                    MessageBox.Show(this, ex.Message, "Connection failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _connect.Enabled = !_scope.IsConnected;
            }
        }

        private async Task DisconnectAsync()
        {
            _refreshTimer.Stop();
            _stateTimer.Stop();
            try { await _scope.DisconnectAsync(); }
            catch (Exception ex) { AppendLog(IoDirection.Error, ex.Message); }

            SetConnectedUi(false);
            _identity.Text = "No instrument connected";
            _identity.ForeColor = Theme.TextDim;
            UpdateTopStrip();
            _screen.ClearImage();
            _screen.Message = "Not connected";
            SetAcqState("IDLE", Theme.TextDim);
        }

        private void SetConnectedUi(bool connected)
        {
            _connect.Enabled = !connected;
            _disconnect.Enabled = connected;
            _address.Enabled = !connected;
            _transport.Enabled = !connected;

            if (_groups != null)
                foreach (var g in _groups) g.Enabled = connected;

            foreach (var channel in _channels)
                if (channel != null) channel.SetInteractive(connected);

            if (connected) SetAcqState("RUNNING", Theme.Run);
        }

        // =============================================================== state

        private async Task ReloadStateAsync()
        {
            if (!_scope.IsConnected) return;
            try
            {
                ScopeState state = await _scope.ReadStateAsync();
                ApplyState(state);
            }
            catch (Exception ex)
            {
                AppendLog(IoDirection.Error, "Could not read the current setup: " + ex.Message);
            }
        }

        private void ApplyState(ScopeState state)
        {
            _suspend = true;
            try
            {
                for (int i = 0; i < 4; i++) _channels[i].Apply(state.Channels[i]);

                UiFactory.SelectOrInsertValue(_timeScale, state.TimeScale, "s");
                _delay.Step = state.TimeScale;
                _delay.SetValueSilently(state.TimePosition);
                UiFactory.SelectScpi(_timeReference, state.TimeReference);

                SetSweepUi(state.TriggerSweep.ToUpperInvariant().StartsWith("NORM") ? "NORM" : "AUTO");
                UiFactory.SelectScpi(_triggerType, state.TriggerMode);
                UiFactory.SelectScpi(_triggerSource, state.TriggerSource);
                SetSlopeUi(SlopeIndexFor(state.TriggerSlope));
                _triggerLevel.Step = Math.Max(state.Channels[0].Scale / 5.0, 1e-4);
                _triggerLevel.SetValueSilently(state.TriggerLevel);
                UiFactory.SelectScpi(_triggerCoupling, state.TriggerCoupling);

                UiFactory.SelectScpi(_acquireType, state.AcquireType);
                UiFactory.SelectValue(_averageCount, state.AcquireCount);
                _averageCount.Enabled = state.AcquireType.ToUpperInvariant().StartsWith("AVER");

                UpdateEdgeControlsEnabled(state.TriggerMode);
            }
            finally { _suspend = false; }
        }

        private static int SlopeIndexFor(string slope)
        {
            string s = (slope ?? string.Empty).ToUpperInvariant();
            if (s.StartsWith("NEG")) return 1;
            if (s.StartsWith("EITH")) return 2;
            if (s.StartsWith("ALT")) return 3;
            return 0;
        }

        private void SetSweepUi(string sweep)
        {
            bool auto = sweep == "AUTO";
            UiFactory.SetKeyActive(_sweepAuto, auto, Theme.Accent);
            UiFactory.SetKeyActive(_sweepNormal, !auto, Theme.Accent);
        }

        private void SetSlopeUi(int index)
        {
            for (int i = 0; i < _slopeKeys.Length; i++)
                UiFactory.SetKeyActive(_slopeKeys[i], i == index, Theme.Accent);
        }

        private void UpdateEdgeControlsEnabled(string mode)
        {
            bool edge = (mode ?? string.Empty).ToUpperInvariant().StartsWith("EDGE");
            _triggerSource.Enabled = edge;
            _triggerLevel.Enabled = edge;
            _levelMidpoint.Enabled = edge;
            foreach (var key in _slopeKeys) key.Enabled = edge;
        }

        private void SetAcqState(string text, Color color)
        {
            _acqState.Text = text;
            _acqState.ForeColor = color;
        }

        // ========================================================== screenshot

        private void ApplyRefreshTimer()
        {
            _refreshTimer.Stop();
            if (!_autoRefresh.Checked || !_scope.IsConnected) return;
            _refreshTimer.Interval = (int)((Choice)_refreshInterval.SelectedItem).Value;
            _refreshTimer.Start();
        }

        private void ApplySyncTimer()
        {
            _stateTimer.Stop();
            if (!_followPanel.Checked || !_scope.IsConnected) return;
            _stateTimer.Interval = (int)((Choice)_syncInterval.SelectedItem).Value;
            _stateTimer.Start();
        }

        /// <summary>
        /// Re-reads the instrument settings so the panel follows anyone working
        /// at the bench. Skipped whenever it would fight the person at this end.
        /// </summary>
        private async Task SyncStateAsync()
        {
            if (!_scope.IsConnected || _syncingState) return;
            if (IsUserEditing()) return;

            // A command just went out. The instrument may not have settled, and
            // reading back now could snap a control away under the user's hand.
            if ((DateTime.UtcNow - _lastUserCommand).TotalSeconds < 2) return;

            _syncingState = true;
            try
            {
                ScopeState state = await _scope.ReadStateAsync(quiet: true);
                ApplyState(state);
            }
            catch (Exception ex)
            {
                AppendLog(IoDirection.Error, "Settings sync failed: " + ex.Message);
            }
            finally { _syncingState = false; }
        }

        /// <summary>True when overwriting the controls would interrupt someone.</summary>
        private bool IsUserEditing()
        {
            if (!ContainsFocus) return false;      // window is in the background

            Control focused = FindFocused(this);
            if (focused == null) return false;
            if (focused is TextBox && focused != _command) return true;

            var combo = focused as ComboBox;
            return combo != null && combo.DroppedDown;
        }

        private static Control FindFocused(Control root)
        {
            var container = root as ContainerControl;
            while (container != null)
            {
                Control active = container.ActiveControl;
                if (active == null) return container;
                var nested = active as ContainerControl;
                if (nested == null) return active;
                container = nested;
            }
            return null;
        }

        private async Task CaptureScreenAsync(bool silent)
        {
            if (!_scope.IsConnected || _capturing) return;
            _capturing = true;
            try
            {
                byte[] png = await _scope.CaptureScreenAsync(_inkSaver.Checked);
                _lastPng = png;

                using (var stream = new MemoryStream(png))
                using (Image decoded = Image.FromStream(stream))
                {
                    _screen.Image = new Bitmap(decoded);
                }

                _captureFailures = 0;
                await UpdateMeasurementsAsync();
            }
            catch (Exception ex)
            {
                _captureFailures++;
                AppendLog(IoDirection.Error, "Screen capture failed: " + ex.Message);

                // A single miss is usually the instrument being busy. Only stop
                // refreshing once it is clearly not coming back.
                if (_captureFailures >= 3 && _autoRefresh.Checked)
                {
                    _autoRefresh.Checked = false;
                    AppendLog(IoDirection.Error,
                        "Auto refresh switched off after three failures. Tick it again to resume.");
                }
                if (!silent)
                    MessageBox.Show(this, ex.Message, "Screen capture failed",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { _capturing = false; }
        }

        private void SaveScreenshot()
        {
            if (_lastPng == null)
            {
                AppendLog(IoDirection.Error, "Capture a screen first, then save it.");
                return;
            }

            using (var dialog = new SaveFileDialog
            {
                Filter = "PNG image|*.png",
                FileName = "MSOX3024G_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllBytes(dialog.FileName, _lastPng);
                    AppendLog(IoDirection.Info, "Saved " + dialog.FileName);
                }
                catch (Exception ex)
                {
                    AppendLog(IoDirection.Error, "Could not save the file: " + ex.Message);
                }
            }
        }

        private async Task UpdateMeasurementsAsync()
        {
            if (!_scope.IsConnected || _measureChannel.SelectedItem == null) return;
            string source = ((Choice)_measureChannel.SelectedItem).Scpi;
            try
            {
                double vpp = await _scope.MeasureAsync("VPP", source);
                double freq = await _scope.MeasureAsync("FREQuency", source);
                _vpp.Text = "Vpp  " + (Math.Abs(vpp) > 1e30 ? "----" : Eng.Format(vpp, "V"));
                _frequency.Text = "Freq " + (Math.Abs(freq) > 1e30 ? "----" : Eng.Format(freq, "Hz"));
            }
            catch (Exception ex)
            {
                _vpp.Text = "Vpp  ----";
                _frequency.Text = "Freq ----";
                AppendLog(IoDirection.Error, "Measurement failed: " + ex.Message);
            }
        }

        // ============================================================= console

        private void SendRawCommand()
        {
            string line = _command.Text.Trim();
            if (line.Length == 0) return;
            _command.Clear();
            Do(() => _scope.SendRawAsync(line));   // the reply is logged by the SCPI layer
        }

        private void OnIo(IoDirection direction, string text)
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => AppendLog(direction, text))); }
                catch (ObjectDisposedException) { }
                return;
            }
            AppendLog(direction, text);
        }

        private void AppendLog(IoDirection direction, string text)
        {
            if (_log == null || _log.IsDisposed) return;

            string prefix;
            Color color;
            switch (direction)
            {
                case IoDirection.Tx: prefix = "\u2192 "; color = Theme.Accent; break;
                case IoDirection.Rx: prefix = "\u2190 "; color = Theme.Text; break;
                case IoDirection.Error: prefix = "!  "; color = Color.FromArgb(240, 110, 100); break;
                default: prefix = "·  "; color = Theme.TextDim; break;
            }

            if (_log.Lines.Length > 900)
                _log.Lines = _log.Lines.Skip(400).ToArray();

            _log.SelectionStart = _log.TextLength;
            _log.SelectionLength = 0;
            _log.SelectionColor = color;
            _log.AppendText(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + prefix + text + "\n");
            _log.SelectionColor = _log.ForeColor;
            ScrollLogToEnd();
        }

        private void ScrollLogToEnd()
        {
            // A hidden RichTextBox has no window handle yet, and scrolling one
            // throws. The text is still captured either way.
            if (_log == null || !_log.IsHandleCreated || !_log.Visible) return;
            try
            {
                _log.SelectionStart = _log.TextLength;
                _log.ScrollToCaret();
            }
            catch (Exception)
            {
            }
        }

        private void SaveSettings()
        {
            try
            {
                _settings.Address = _address.Text.Trim();
                if (_transport.SelectedItem != null)
                    _settings.Transport = ((Choice)_transport.SelectedItem).Scpi;
                if (_refreshInterval.SelectedItem != null)
                    _settings.RefreshIntervalMs = (int)((Choice)_refreshInterval.SelectedItem).Value;
                _settings.AutoRefresh = _autoRefresh.Checked;
                _settings.InkSaver = _inkSaver.Checked;
                _settings.CheckErrors = _errorCheck.Checked;
                _settings.ShowConsole = _console.Visible;
                _settings.ShowTopBar = _topBar.Visible;
                _settings.FollowPanel = _followPanel.Checked;
                if (_syncInterval.SelectedItem != null)
                    _settings.SyncIntervalMs = (int)((Choice)_syncInterval.SelectedItem).Value;
                _settings.Maximized = WindowState == FormWindowState.Maximized;

                // Store the restored size, not the maximised one.
                Size size = WindowState == FormWindowState.Normal ? ClientSize : RestoreBounds.Size;
                _settings.WindowWidth = size.Width;
                _settings.WindowHeight = size.Height;

                _settings.Save();
            }
            catch (Exception)
            {
                // Never block shutdown over preferences.
            }
        }

        /// <summary>Runs an instrument action and reports anything that goes wrong in the log.</summary>
        private async void Do(Func<Task> action)
        {
            if (!_scope.IsConnected)
            {
                AppendLog(IoDirection.Error, "Not connected. Press Connect first.");
                return;
            }
            _lastUserCommand = DateTime.UtcNow;
            try { await action(); }
            catch (Exception ex) { AppendLog(IoDirection.Error, ex.Message); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _refreshTimer.Stop();
            _stateTimer.Stop();
            SaveSettings();
            try { _scope.Dispose(); } catch { }
            base.OnFormClosing(e);
        }
    }
}
