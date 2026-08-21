using System;
using System.Drawing;
using System.Windows.Forms;
using ScopeControl.Instrument;

namespace ScopeControl.UI
{
    /// <summary>
    /// Cursor control. Typing a value is always exact; clicking on the display
    /// is quicker but depends on knowing where the graticule sits inside the
    /// captured image, which differs by model, so that is calibrated once here.
    /// </summary>
    public sealed class CursorsTab : UserControl
    {
        private readonly ComboBox _mode;
        private readonly ComboBox _sourceA;
        private readonly ComboBox _sourceB;
        private readonly EngBox _x1;
        private readonly EngBox _x2;
        private readonly EngBox _y1;
        private readonly EngBox _y2;
        private readonly Label _xDelta;
        private readonly Label _xRate;
        private readonly Label _yDelta;
        private readonly ComboBox _clickTarget;
        private readonly CheckBox _showGuide;
        private readonly EngBox _gLeft, _gTop, _gRight, _gBottom;

        private readonly Label _clickHint;
        private string _focused;
        private bool _updating;

        public CursorsTab()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Theme.Chassis;
            AutoScroll = true;

            // ---- mode and sources
            Controls.Add(UiFactory.Caption("Mode", 12, 10, 56));
            _mode = UiFactory.Combo(72, 10, 140);
            UiFactory.FillCombo(_mode,
                new Choice("Off", "OFF"),
                new Choice("Manual", "MANual"),
                new Choice("Track waveform", "WAVeform"),
                new Choice("Measurement", "MEASurement"));
            _mode.SelectedIndexChanged += (s, e) =>
            {
                if (_mode.SelectedItem == null) return;
                string mode = ((Choice)_mode.SelectedItem).Scpi;
                UpdateEnabledState(mode);
                if (_updating) return;
                ModeChanged?.Invoke(mode);
            };

            Controls.Add(UiFactory.Caption("X1/Y1 on", 12, 38, 56));
            _sourceA = UiFactory.Combo(72, 38, 140);
            FillSources(_sourceA);
            _sourceA.SelectedIndexChanged += (s, e) =>
            {
                if (_updating || _sourceA.SelectedItem == null) return;
                SourceChanged?.Invoke(1, ((Choice)_sourceA.SelectedItem).Scpi);
            };

            Controls.Add(UiFactory.Caption("X2/Y2 on", 12, 66, 56));
            _sourceB = UiFactory.Combo(72, 66, 140);
            FillSources(_sourceB);
            _sourceB.SelectedIndexChanged += (s, e) =>
            {
                if (_updating || _sourceB.SelectedItem == null) return;
                SourceChanged?.Invoke(2, ((Choice)_sourceB.SelectedItem).Scpi);
            };

            Controls.AddRange(new Control[] { _mode, _sourceA, _sourceB });

            // ---- positions
            Controls.Add(UiFactory.Caption("X1", 240, 10, 30));
            _x1 = MakeBox(274, 10, "s", 1e-6);
            _x1.ValueCommitted += (s, e) => { if (!_updating) XChanged?.Invoke(1, _x1.Value); };

            Controls.Add(UiFactory.Caption("X2", 240, 38, 30));
            _x2 = MakeBox(274, 38, "s", 1e-6);
            _x2.ValueCommitted += (s, e) => { if (!_updating) XChanged?.Invoke(2, _x2.Value); };

            Controls.Add(UiFactory.Caption("Y1", 240, 66, 30));
            _y1 = MakeBox(274, 66, "V", 0.1);
            _y1.ValueCommitted += (s, e) => { if (!_updating) YChanged?.Invoke(1, _y1.Value); };

            Controls.Add(UiFactory.Caption("Y2", 240, 94, 30));
            _y2 = MakeBox(274, 94, "V", 0.1);
            _y2.ValueCommitted += (s, e) => { if (!_updating) YChanged?.Invoke(2, _y2.Value); };

            // Putting the caret in a position field aims the next click at that
            // cursor. Quicker than choosing from the dropdown, and it reads the
            // way you would expect: click where the value should be.
            WireFocus(_x1, "X1");
            WireFocus(_x2, "X2");
            WireFocus(_y1, "Y1");
            WireFocus(_y2, "Y2");

            Controls.AddRange(new Control[] { _x1, _x2, _y1, _y2 });

            // ---- readouts
            Controls.Add(new Label
            {
                Text = "DELTAS",
                Location = new Point(452, 10),
                Size = new Size(120, 14),
                ForeColor = Theme.Accent,
                Font = Theme.Header
            });
            _xDelta = UiFactory.Value("ΔX  ----", 452, 30, 170, Theme.Text);
            _xRate = UiFactory.Value("1/ΔX ----", 452, 54, 170, Theme.Text);
            _yDelta = UiFactory.Value("ΔY  ----", 452, 78, 170, Theme.Text);
            Controls.AddRange(new Control[] { _xDelta, _xRate, _yDelta });

            // ---- click to place
            Controls.Add(new Label
            {
                Text = "CLICK ON THE DISPLAY",
                Location = new Point(648, 10),
                Size = new Size(200, 14),
                ForeColor = Theme.Accent,
                Font = Theme.Header
            });

            Controls.Add(UiFactory.Caption("A click sets", 648, 30, 70));
            _clickTarget = UiFactory.Combo(724, 30, 140);
            UiFactory.FillCombo(_clickTarget,
                new Choice("Nothing", "NONE"),
                new Choice("X1 and Y1", "XY1"),
                new Choice("X2 and Y2", "XY2"),
                new Choice("X1 only", "X1"),
                new Choice("X2 only", "X2"),
                new Choice("Y1 only", "Y1"),
                new Choice("Y2 only", "Y2"));
            _clickTarget.SelectedIndexChanged += (s, e) =>
            {
                _focused = null;                 // an explicit choice overrides focus
                UpdateClickHint();
                ClickTargetChanged?.Invoke(EffectiveClickTarget);
            };

            _clickHint = new Label
            {
                Location = new Point(648, 168),
                Size = new Size(360, 16),
                ForeColor = Theme.TextDim,
                Font = Theme.UiBold
            };

            _showGuide = UiFactory.Check("Show graticule guide", 648, 58, 190);
            _showGuide.CheckedChanged += (s, e) => GuideToggled?.Invoke(_showGuide.Checked);

            var calibrationNote = new Label
            {
                Text = "Positions come from the captured image, so the box below must match the\r\n" +
                       "graticule. Switch the guide on and nudge these until it lines up. Once.",
                Location = new Point(648, 80),
                Size = new Size(360, 30),
                ForeColor = Theme.TextDim,
                Font = new Font("Segoe UI", 6.75f)
            };

            Controls.Add(UiFactory.Caption("Left", 648, 112, 34));
            _gLeft = MakeBox(686, 112, "%", 0.5);
            Controls.Add(UiFactory.Caption("Top", 830, 112, 30));
            _gTop = MakeBox(864, 112, "%", 0.5);
            Controls.Add(UiFactory.Caption("Right", 648, 138, 34));
            _gRight = MakeBox(686, 138, "%", 0.5);
            Controls.Add(UiFactory.Caption("Bottom", 830, 138, 34));
            _gBottom = MakeBox(864, 138, "%", 0.5);

            foreach (var box in new[] { _gLeft, _gTop, _gRight, _gBottom })
            {
                box.Width = 130;
                box.Minimum = 0;
                box.Maximum = 100;
                box.ValueCommitted += (s, e) => { if (!_updating) GraticuleChanged?.Invoke(Graticule); };
            }

            Controls.AddRange(new Control[]
            {
                _clickTarget, _showGuide, calibrationNote, _gLeft, _gTop, _gRight, _gBottom,
                _clickHint
            });

            UpdateEnabledState("OFF");
            UpdateClickHint();
        }

        private void WireFocus(EngBox box, string target)
        {
            box.EntryFocused += (s, e) =>
            {
                _focused = target;
                UpdateClickHint();
                ClickTargetChanged?.Invoke(EffectiveClickTarget);
            };
        }

        /// <summary>
        /// What a click on the display will set. A focused position field wins
        /// over the dropdown, so the two never disagree about what is happening.
        /// </summary>
        public string EffectiveClickTarget
        {
            get
            {
                if (_x1.EntryHasFocus) return "X1";
                if (_x2.EntryHasFocus) return "X2";
                if (_y1.EntryHasFocus) return "Y1";
                if (_y2.EntryHasFocus) return "Y2";
                return ClickTarget;
            }
        }

        /// <summary>Shows a value placed by a click, without echoing it back out.</summary>
        public void ShowPosition(string target, double value)
        {
            EngBox box = target == "X1" ? _x1
                : target == "X2" ? _x2
                : target == "Y1" ? _y1
                : target == "Y2" ? _y2 : null;
            box?.SetValueSilently(value);
        }

        private void UpdateClickHint()
        {
            string target = EffectiveClickTarget;
            _clickHint.Text = target == "NONE"
                ? "A click on the display does nothing right now."
                : "A click on the display sets " + target + ".";
            _clickHint.ForeColor = target == "NONE" ? Theme.TextDim : Theme.Accent;
        }

        private static EngBox MakeBox(int x, int y, string unit, double step)
        {
            return new EngBox
            {
                Location = new Point(x, y),
                Size = new Size(160, 22),
                Unit = unit,
                Step = step,
                Minimum = -1e9,
                Maximum = 1e9
            };
        }

        private static void FillSources(ComboBox combo)
        {
            UiFactory.FillCombo(combo,
                new Choice("CH 1", "CHANnel1"),
                new Choice("CH 2", "CHANnel2"),
                new Choice("CH 3", "CHANnel3"),
                new Choice("CH 4", "CHANnel4"),
                new Choice("Math", "FUNCtion"));
        }

        public event Action<string> ModeChanged;
        public event Action<int, string> SourceChanged;
        public event Action<int, double> XChanged;
        public event Action<int, double> YChanged;
        public event Action<string> ClickTargetChanged;
        public event Action<bool> GuideToggled;
        public event Action<RectangleF> GraticuleChanged;

        public string ClickTarget =>
            _clickTarget.SelectedItem == null ? "NONE" : ((Choice)_clickTarget.SelectedItem).Scpi;

        /// <summary>The graticule rectangle as fractions of the captured image.</summary>
        public RectangleF Graticule
        {
            get
            {
                float left = (float)(_gLeft.Value / 100.0);
                float top = (float)(_gTop.Value / 100.0);
                float right = (float)(_gRight.Value / 100.0);
                float bottom = (float)(_gBottom.Value / 100.0);
                if (right <= left) right = left + 0.01f;
                if (bottom <= top) bottom = top + 0.01f;
                return new RectangleF(left, top, right - left, bottom - top);
            }
            set
            {
                _updating = true;
                try
                {
                    _gLeft.SetValueSilently(value.Left * 100.0);
                    _gTop.SetValueSilently(value.Top * 100.0);
                    _gRight.SetValueSilently(value.Right * 100.0);
                    _gBottom.SetValueSilently(value.Bottom * 100.0);
                }
                finally { _updating = false; }
            }
        }

        private void UpdateEnabledState(string mode)
        {
            string m = (mode ?? string.Empty).ToUpperInvariant();
            bool on = !m.StartsWith("OFF");
            bool manualY = m.StartsWith("MAN");      // Y follows the trace in WAVeform mode

            _sourceA.Enabled = on;
            _sourceB.Enabled = on;
            _x1.Enabled = on;
            _x2.Enabled = on;
            _y1.Enabled = manualY;
            _y2.Enabled = manualY;
        }

        public void Apply(MarkerState state)
        {
            _updating = true;
            try
            {
                UiFactory.SelectScpi(_mode, state.Mode);
                UiFactory.SelectScpi(_sourceA, state.X1Y1Source);
                UiFactory.SelectScpi(_sourceB, state.X2Y2Source);
                _x1.SetValueSilently(state.X1);
                _x2.SetValueSilently(state.X2);
                _y1.SetValueSilently(state.Y1);
                _y2.SetValueSilently(state.Y2);
                UpdateEnabledState(state.Mode);
            }
            finally { _updating = false; }
        }

        public void SetDeltas(double xDelta, double yDelta)
        {
            bool xUsable = Math.Abs(xDelta) > 1e-18 && Math.Abs(xDelta) < 1e30;
            _xDelta.Text = "ΔX  " + (xUsable ? Eng.Format(xDelta, "s") : "----");
            _xRate.Text = "1/ΔX " + (xUsable ? Eng.Format(1.0 / xDelta, "Hz") : "----");
            _yDelta.Text = "ΔY  " + (Math.Abs(yDelta) < 1e30 ? Eng.Format(yDelta, "V") : "----");
        }

        public void SetInteractive(bool enabled)
        {
            foreach (Control c in Controls) c.Enabled = enabled;
            if (enabled && _mode.SelectedItem != null)
                UpdateEnabledState(((Choice)_mode.SelectedItem).Scpi);
        }
    }
}
