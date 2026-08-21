using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ScopeControl.Instrument;

namespace ScopeControl.UI
{
    /// <summary>
    /// Adds measurements to the instrument's own on-screen readout, so they turn
    /// up in the mirrored display rather than only in this window.
    ///
    /// Clicking a measurement that is already showing removes it. The command
    /// set has no "delete one measurement", so removal is done by clearing the
    /// lot and re-sending the survivors; this tab keeps the list needed for that.
    /// </summary>
    public sealed class MeasurementsTab : UserControl
    {
        private readonly ComboBox _source;
        private readonly ComboBox _interval;
        private readonly ComboBox _type;

        private readonly List<ActiveMeasurement> _active = new List<ActiveMeasurement>();
        private bool _enabled;
        private readonly Dictionary<string, Button> _keys = new Dictionary<string, Button>();
        private readonly HashSet<string> _unsupported = new HashSet<string>();
        private readonly ToolTip _tips = new ToolTip();

        public MeasurementsTab()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Theme.Chassis;
            AutoScroll = true;

            Controls.Add(UiFactory.Caption("Source", 12, 8, 50));
            _source = UiFactory.Combo(66, 8, 100);
            UiFactory.FillCombo(_source,
                new Choice("CH 1", "CHANnel1"),
                new Choice("CH 2", "CHANnel2"),
                new Choice("CH 3", "CHANnel3"),
                new Choice("CH 4", "CHANnel4"),
                new Choice("Math", "FUNCtion"));
            _source.SelectedIndexChanged += (s, e) => RefreshKeyStates();

            Controls.Add(UiFactory.Caption("Interval", 180, 8, 50));
            _interval = UiFactory.Combo(234, 8, 90);
            UiFactory.FillCombo(_interval,
                new Choice("Display", "DISPlay"),
                new Choice("Cycle", "CYCLe"));

            Controls.Add(UiFactory.Caption("RMS type", 338, 8, 56));
            _type = UiFactory.Combo(398, 8, 66);

            UiFactory.FillCombo(_type,
                new Choice("AC", "AC"),
                new Choice("DC", "DC"));

            var clear = UiFactory.Key("Clear all", 478, 8, 96, 22);
            clear.Font = Theme.Ui;
            clear.Click += (s, e) =>
            {
                _active.Clear();
                RefreshKeyStates();
                ClearRequested?.Invoke();
            };

            var hint = new Label
            {
                Text = "Click to add to the instrument display, click again to remove it.",
                Location = new Point(588, 11),
                Size = new Size(400, 16),
                ForeColor = Theme.TextDim,
                Font = Theme.Ui
            };

            Controls.AddRange(new Control[] { _source, _interval, _type, clear, hint });

            AddGroup("Voltage", 12, 36, Measurements.Voltage);
            AddGroup("Time", 356, 36, Measurements.Time);
            AddGroup("Counting", 700, 36, Measurements.Counting);
        }

        /// <summary>Raised with the single measurement to add.</summary>
        public event Action<ActiveMeasurement> AddRequested;

        /// <summary>
        /// Raised when one has been removed. The argument is everything that
        /// should remain, to be re-sent after a clear.
        /// </summary>
        public event Action<List<ActiveMeasurement>> RebuildRequested;

        public event Action ClearRequested;

        private void AddGroup(string title, int x, int y, Measurement[] items)
        {
            Controls.Add(new Label
            {
                Text = title.ToUpperInvariant(),
                Location = new Point(x, y),
                Size = new Size(200, 14),
                ForeColor = Theme.Accent,
                Font = Theme.Header
            });

            const int keyWidth = 106;
            const int keyHeight = 26;
            const int gap = 3;
            const int columns = 3;

            for (int i = 0; i < items.Length; i++)
            {
                Measurement item = items[i];
                int column = i % columns;
                int row = i / columns;

                var key = UiFactory.Key(item.Text,
                    x + column * (keyWidth + gap),
                    y + 18 + row * (keyHeight + gap),
                    keyWidth, keyHeight);
                key.Font = Theme.Ui;
                key.Click += (s, e) => Toggle(item);

                _keys[item.Keyword] = key;
                Controls.Add(key);
            }
        }

        private void Toggle(Measurement item)
        {
            string source = SelectedSource;
            int existing = _active.FindIndex(a => a.Kind.Keyword == item.Keyword && a.Source == source);

            if (existing >= 0)
            {
                _active.RemoveAt(existing);
                RefreshKeyStates();
                RebuildRequested?.Invoke(new List<ActiveMeasurement>(_active));
                return;
            }

            var added = new ActiveMeasurement(item, source, SelectedInterval, SelectedType);
            _active.Add(added);
            RefreshKeyStates();
            AddRequested?.Invoke(added);
        }

        /// <summary>Lights the keys already showing for the selected source.</summary>
        private void RefreshKeyStates()
        {
            string source = SelectedSource;
            foreach (var pair in _keys)
            {
                bool on = _active.Exists(a => a.Kind.Keyword == pair.Key && a.Source == source);
                UiFactory.SetKeyActive(pair.Value, on, Theme.Accent);
            }
        }

        /// <summary>
        /// The instrument drops the oldest measurement once its display is full,
        /// so what we think is showing can drift. Called after a clear.
        /// </summary>
        public void ForgetAll()
        {
            _active.Clear();
            RefreshKeyStates();
        }

        public string SelectedSource =>
            _source.SelectedItem == null ? "CHANnel1" : ((Choice)_source.SelectedItem).Scpi;

        public string SelectedInterval =>
            _interval.SelectedItem == null ? "DISPlay" : ((Choice)_interval.SelectedItem).Scpi;

        public string SelectedType =>
            _type.SelectedItem == null ? "AC" : ((Choice)_type.SelectedItem).Scpi;

        /// <summary>
        /// Dims measurements the selected model is not known to have. They are
        /// left visible rather than removed, so the grid keeps its shape and it
        /// is obvious that something exists but is unavailable.
        /// </summary>
        public void ApplyProfile(InstrumentProfile profile)
        {
            _unsupported.Clear();
            foreach (var pair in _keys)
            {
                if (profile.SupportsMeasurement(pair.Key)) continue;
                _unsupported.Add(pair.Key);
                _tips.SetToolTip(pair.Value, "Not available on the selected model");
            }
            ApplyEnabledState();
        }

        public void SetInteractive(bool enabled)
        {
            _enabled = enabled;
            foreach (Control c in Controls) c.Enabled = enabled;
            ApplyEnabledState();
        }

        private void ApplyEnabledState()
        {
            foreach (var pair in _keys)
            {
                bool usable = _enabled && !_unsupported.Contains(pair.Key);
                pair.Value.Enabled = usable;
                pair.Value.ForeColor = usable ? Theme.Text : Theme.Border;
            }
        }
    }
}
