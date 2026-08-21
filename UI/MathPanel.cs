using System;
using System.Drawing;
using System.Windows.Forms;
using ScopeControl.Instrument;

namespace ScopeControl.UI
{
    /// <summary>
    /// The math waveform, sitting in the channel row alongside CH1-4.
    ///
    /// It deliberately carries fewer controls than a real channel: coupling,
    /// probe attenuation, bandwidth limit and invert are properties of an input
    /// amplifier, and math has no input. Scale and offset are the only vertical
    /// settings the instrument accepts for it.
    /// </summary>
    public sealed class MathPanel : UserControl
    {
        private const int DesignWidth = 250;
        private const int DesignHeight = 134;

        private readonly Button _key;
        private readonly Button _selectKey;
        private readonly ToolTip _tips = new ToolTip();
        private readonly ComboBox _scale;
        private readonly EngBox _offset;
        private readonly Label _formula;

        private bool _on;
        private bool _selected;
        private bool _updating;

        public MathPanel()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;

            BackColor = Theme.Group;
            Size = new Size(DesignWidth, DesignHeight);
            MinimumSize = new Size(180, DesignHeight);

            _key = new Button
            {
                Text = "MATH",
                Location = new Point(6, 4),
                Size = new Size(DesignWidth - 48, 26),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                TabStop = false
            };
            _key.FlatAppearance.BorderSize = 2;
            _key.Click += (s, e) => { IsOn = !IsOn; if (!_updating) EnabledChanged?.Invoke(_on); };
            _tips.SetToolTip(_key, "Turn the math waveform on or off");

            _selectKey = UiFactory.Key("Grid", DesignWidth - 38, 4, 32, 26);
            _selectKey.Font = new Font("Segoe UI", 7f, FontStyle.Bold);
            _selectKey.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _selectKey.Click += (s, e) => SelectRequested?.Invoke();
            _tips.SetToolTip(_selectKey,
                "Show the math waveform's labels down the left edge of the screen");

            var scaleCaption = UiFactory.Caption("/div", 6, 36, 40);
            _scale = UiFactory.Combo(48, 36, DesignWidth - 54);
            _scale.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            foreach (double v in Eng.Sequence125(1e-3, 1000))
                _scale.Items.Add(new Choice(Eng.Format(v, ""), v));
            UiFactory.SelectValue(_scale, 1.0);
            _scale.SelectedIndexChanged += (s, e) =>
            {
                if (_updating || _scale.SelectedItem == null) return;
                double v = ((Choice)_scale.SelectedItem).Value;
                _offset.Step = v;
                ScaleChanged?.Invoke(v);
            };

            var offsetCaption = UiFactory.Caption("Offset", 6, 61, 40);
            _offset = new EngBox
            {
                Location = new Point(48, 61),
                Size = new Size(DesignWidth - 54, 22),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Unit = "",
                Step = 1.0,
                Minimum = -1e6,
                Maximum = 1e6
            };
            _offset.ValueCommitted += (s, e) => { if (!_updating) OffsetChanged?.Invoke(_offset.Value); };

            _formula = new Label
            {
                Text = "CH1 + CH2",
                Location = new Point(6, 90),
                Size = new Size(DesignWidth - 12, 18),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Theme.Math,
                Font = Theme.Readout,
                TextAlign = ContentAlignment.MiddleCenter
            };

            var hint = new Label
            {
                Text = "operator on the Math tab",
                Location = new Point(6, 110),
                Size = new Size(DesignWidth - 12, 16),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                ForeColor = Theme.TextDim,
                Font = new Font("Segoe UI", 6.75f),
                TextAlign = ContentAlignment.MiddleCenter
            };

            Controls.AddRange(new Control[]
            {
                _key, _selectKey, scaleCaption, _scale, offsetCaption, _offset, _formula, hint
            });

            IsOn = false;
            Paint += (s, e) =>
            {
                using (var pen = new Pen(_selected ? Theme.Math : Theme.Border, _selected ? 2 : 1))
                    e.Graphics.DrawRectangle(pen, 1, 1, Width - 3, Height - 3);
            };
        }

        public event Action<bool> EnabledChanged;
        public event Action SelectRequested;
        public event Action<double> ScaleChanged;
        public event Action<double> OffsetChanged;

        public bool IsOn
        {
            get => _on;
            set
            {
                _on = value;
                _key.BackColor = _on ? Theme.Math : Theme.Key;
                _key.ForeColor = _on ? UiFactory.PickReadableText(Theme.Math) : Theme.Math;
                _key.FlatAppearance.BorderColor = Theme.Math;
                _key.FlatAppearance.MouseOverBackColor = _on
                    ? ControlPaint.Light(Theme.Math, 0.2f)
                    : Theme.KeyHover;
            }
        }

        public bool IsSelected
        {
            get => _selected;
            set { _selected = value; Invalidate(); }
        }

        public void Apply(MathState state)
        {
            _updating = true;
            try
            {
                IsOn = state.Display;
                UiFactory.SelectOrInsertValue(_scale, state.Scale, "");
                _offset.Step = state.Scale;
                _offset.SetValueSilently(state.Offset);
                _formula.Text = Describe(state);
            }
            finally { _updating = false; }
        }

        /// <summary>Renders the current operation the way the instrument labels it.</summary>
        private static string Describe(MathState state)
        {
            string a = Pretty(state.Source1);
            string b = Pretty(state.Source2);
            string op = (state.Operation ?? string.Empty).ToUpperInvariant();

            if (op.StartsWith("ADD")) return a + " + " + b;
            if (op.StartsWith("SUBT")) return a + " - " + b;
            if (op.StartsWith("MULT")) return a + " * " + b;
            if (op.StartsWith("DIV")) return a + " / " + b;
            if (op.StartsWith("FFTP")) return "FFT phase " + a;
            if (op.StartsWith("FFT")) return "FFT " + a;
            if (op.StartsWith("INT")) return "\u222B " + a;
            if (op.StartsWith("DIFF")) return "d/dt " + a;
            if (op.StartsWith("SQRT")) return "\u221A " + a;
            return op + " " + a;
        }

        private static string Pretty(string source)
        {
            string s = (source ?? string.Empty).ToUpperInvariant().Replace("ANNEL", "");
            return s.StartsWith("CH") ? "CH" + new string(Array.FindAll(s.ToCharArray(), char.IsDigit)) : s;
        }

        public void SetInteractive(bool enabled)
        {
            foreach (Control c in Controls) c.Enabled = enabled;
        }
    }
}
