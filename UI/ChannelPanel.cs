using System;
using System.Drawing;
using System.Windows.Forms;
using ScopeControl.Instrument;

namespace ScopeControl.UI
{
    /// <summary>
    /// One vertical channel: the coloured channel key turns the trace on and off,
    /// with volts/div, offset, coupling, probe and bandwidth limit below it.
    /// Kept compact so the strip steals as little height as possible from the
    /// display above.
    /// </summary>
    public sealed class ChannelPanel : UserControl
    {
        private const int DesignWidth = 250;
        private const int DesignHeight = 134;

        private readonly int _number;
        private readonly Color _color;

        private readonly Button _key;
        private readonly Button _selectKey;
        private readonly ToolTip _tips = new ToolTip();
        private readonly ComboBox _scale;
        private readonly EngBox _offset;
        private readonly ComboBox _coupling;
        private readonly ComboBox _probe;
        private readonly CheckBox _bwLimit;
        private readonly CheckBox _invert;

        private bool _on;
        private bool _updating;
        private bool _selected;

        public ChannelPanel(int number)
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            _number = number;
            _color = Theme.Channel(number);
            BackColor = Theme.Group;
            Padding = new Padding(0);
            // Design size first: anchors measure their margins against it.
            Size = new Size(DesignWidth, DesignHeight);
            MinimumSize = new Size(180, DesignHeight);

            _key = new Button
            {
                Text = "CH " + number,
                Location = new Point(6, 4),
                Size = new Size(DesignWidth - 48, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                TabStop = false
            };
            _key.FlatAppearance.BorderSize = 2;
            _key.Click += (s, e) => { IsOn = !IsOn; RaiseEnabled(); };
            _tips.SetToolTip(_key, "Turn this channel on or off");

            _selectKey = UiFactory.Key("Grid", DesignWidth - 38, 4, 32, 26);
            _selectKey.Font = new Font("Segoe UI", 7f, FontStyle.Bold);
            _selectKey.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _selectKey.Click += (s, e) => SelectRequested?.Invoke(_number);
            _tips.SetToolTip(_selectKey,
                "Show this channel's voltage labels down the left edge of the screen");

            var scaleCaption = UiFactory.Caption("V/div", 6, 36, 40);
            _scale = UiFactory.Combo(48, 36, DesignWidth - 54);
            foreach (double v in Eng.Sequence125(1e-3, 5.0))
                _scale.Items.Add(new Choice(Eng.Format(v, "V"), v));
            _scale.SelectedIndex = _scale.Items.Count - 1;
            _scale.SelectedIndexChanged += (s, e) =>
            {
                if (_updating || _scale.SelectedItem == null) return;
                double v = ((Choice)_scale.SelectedItem).Value;
                _offset.Step = v;                       // one nudge = one division
                ScaleChanged?.Invoke(_number, v);
            };

            var offsetCaption = UiFactory.Caption("Offset", 6, 61, 40);
            _offset = new EngBox
            {
                Location = new Point(48, 61),
                Size = new Size(DesignWidth - 54, 22),
                Unit = "V",
                Step = 1.0,
                Minimum = -200,
                Maximum = 200
            };
            _offset.ValueCommitted += (s, e) => { if (!_updating) OffsetChanged?.Invoke(_number, _offset.Value); };

            var couplingCaption = UiFactory.Caption("Cpl", 6, 86, 40);
            _coupling = UiFactory.Combo(48, 86, 74);
            UiFactory.FillCombo(_coupling, new Choice("DC", "DC"), new Choice("AC", "AC"));
            _coupling.SelectedIndexChanged += (s, e) =>
            {
                if (_updating || _coupling.SelectedItem == null) return;
                CouplingChanged?.Invoke(_number, ((Choice)_coupling.SelectedItem).Scpi);
            };

            _probe = UiFactory.Combo(126, 86, DesignWidth - 132);
            foreach (double a in new[] { 0.1, 1.0, 10.0, 20.0, 100.0, 1000.0 })
                _probe.Items.Add(new Choice(a.ToString("0.###") + " : 1", a));
            _probe.SelectedIndex = 2;
            _probe.SelectedIndexChanged += (s, e) =>
            {
                if (_updating || _probe.SelectedItem == null) return;
                ProbeChanged?.Invoke(_number, ((Choice)_probe.SelectedItem).Value);
            };

            _bwLimit = UiFactory.Check("BW limit", 6, 110, 82);
            _bwLimit.CheckedChanged += (s, e) => { if (!_updating) BwLimitChanged?.Invoke(_number, _bwLimit.Checked); };
            _invert = UiFactory.Check("Invert", 94, 110, 70);
            _invert.CheckedChanged += (s, e) => { if (!_updating) InvertChanged?.Invoke(_number, _invert.Checked); };

            _scale.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _offset.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _probe.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            Controls.AddRange(new Control[]
            {
                _key, _selectKey, scaleCaption, _scale, offsetCaption, _offset,
                couplingCaption, _coupling, _probe, _bwLimit, _invert
            });

            IsOn = false;
            Paint += (s, e) =>
            {
                using (var pen = new Pen(_selected ? _color : Theme.Border, _selected ? 2 : 1))
                    e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
            };
        }

        public int Number => _number;

        public bool IsOn
        {
            get => _on;
            set
            {
                _on = value;
                _key.BackColor = _on ? _color : Theme.Key;
                _key.ForeColor = _on ? UiFactory.PickReadableText(_color) : _color;
                _key.FlatAppearance.BorderColor = _color;
                _key.FlatAppearance.MouseOverBackColor = _on
                    ? ControlPaint.Light(_color, 0.2f)
                    : Theme.KeyHover;
            }
        }

        /// <summary>Marks the channel whose grid labels the instrument is showing.</summary>
        public bool IsSelected
        {
            get => _selected;
            set { _selected = value; Invalidate(); }
        }

        public double Scale => _scale.SelectedItem == null ? 1.0 : ((Choice)_scale.SelectedItem).Value;

        /// <summary>Raised when the user picks this channel as the selected one.</summary>
        public event Action<int> SelectRequested;

        public event Action<int, bool> EnabledChanged;
        public event Action<int, double> ScaleChanged;
        public event Action<int, double> OffsetChanged;
        public event Action<int, string> CouplingChanged;
        public event Action<int, double> ProbeChanged;
        public event Action<int, bool> BwLimitChanged;
        public event Action<int, bool> InvertChanged;

        private void RaiseEnabled()
        {
            if (_updating) return;
            EnabledChanged?.Invoke(_number, _on);
        }

        /// <summary>Loads values read back from the instrument without echoing them out again.</summary>
        public void Apply(ChannelState state)
        {
            _updating = true;
            try
            {
                IsOn = state.Display;
                UiFactory.SelectOrInsertValue(_scale, state.Scale, "V");
                _offset.Step = state.Scale;
                _offset.SetValueSilently(state.Offset);
                UiFactory.SelectScpi(_coupling, state.Coupling);
                UiFactory.SelectValue(_probe, state.Probe);
                _bwLimit.Checked = state.BwLimit;
                _invert.Checked = state.Invert;
            }
            finally { _updating = false; }
        }

        public void SetInteractive(bool enabled)
        {
            foreach (Control c in Controls) c.Enabled = enabled;
        }
    }
}
