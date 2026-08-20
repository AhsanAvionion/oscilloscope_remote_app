using System;
using System.Drawing;
using System.Windows.Forms;
using ScopeControl.Instrument;

namespace ScopeControl.UI
{
    /// <summary>
    /// Value entry that behaves like a front-panel knob: type "1.5 ms" or
    /// "-250mV", or nudge it a step at a time. The zero key re-centres.
    /// </summary>
    public sealed class EngBox : UserControl
    {
        private readonly TextBox _text;
        private readonly Button _down;
        private readonly Button _up;
        private readonly Button _zero;

        private double _value;
        private bool _updating;

        public EngBox()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.Transparent;

            _text = new TextBox
            {
                BackColor = Theme.Field,
                ForeColor = Theme.Text,
                Font = Theme.Mono,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = HorizontalAlignment.Right
            };
            _text.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Up) { Nudge(+1); e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Down) { Nudge(-1); e.SuppressKeyPress = true; }
            };
            _text.Leave += (s, e) => Commit();

            _down = MakeKey("\u25BC", () => Nudge(-1));
            _up = MakeKey("\u25B2", () => Nudge(+1));
            _zero = MakeKey("0", () => { Value = 0; RaiseCommitted(); });

            Controls.Add(_text);
            Controls.Add(_down);
            Controls.Add(_up);
            Controls.Add(_zero);

            Size = new Size(150, 22);   // children exist now, so layout is safe
            UpdateText();
        }

        private Button MakeKey(string glyph, Action action)
        {
            var b = new Button
            {
                Text = glyph,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Key,
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 6.5f, FontStyle.Bold),
                TabStop = false
            };
            b.FlatAppearance.BorderColor = Theme.Border;
            b.FlatAppearance.MouseOverBackColor = Theme.KeyHover;
            b.Click += (s, e) => action();
            return b;
        }

        /// <summary>Unit symbol, e.g. "V" or "s".</summary>
        public string Unit { get; set; } = "V";

        /// <summary>Size of one nudge.</summary>
        public double Step { get; set; } = 0.1;

        public double Minimum { get; set; } = -1e9;
        public double Maximum { get; set; } = 1e9;

        /// <summary>Fires when the user commits a new value (not when code sets it).</summary>
        public event EventHandler ValueCommitted;

        public double Value
        {
            get => _value;
            set
            {
                _value = Clamp(value);
                UpdateText();
            }
        }

        /// <summary>Updates the display without raising ValueCommitted.</summary>
        public void SetValueSilently(double value)
        {
            _updating = true;
            try { Value = value; }
            finally { _updating = false; }
        }

        private double Clamp(double v)
        {
            if (v < Minimum) return Minimum;
            if (v > Maximum) return Maximum;
            return v;
        }

        private void UpdateText()
        {
            if (_text == null) return;
            _text.Text = Eng.Format(_value, Unit);
        }

        private void Nudge(int direction)
        {
            double next = Clamp(_value + direction * Step);
            if (Math.Abs(next - _value) < double.Epsilon) return;
            _value = next;
            UpdateText();
            RaiseCommitted();
        }

        private void Commit()
        {
            if (Eng.TryParse(_text.Text, Unit, out double parsed))
            {
                double next = Clamp(parsed);
                bool changed = Math.Abs(next - _value) > Math.Abs(next) * 1e-9 + 1e-15;
                _value = next;
                UpdateText();
                if (changed) RaiseCommitted();
            }
            else
            {
                UpdateText();   // unreadable entry: put the old value back
            }
        }

        private void RaiseCommitted()
        {
            if (_updating) return;
            ValueCommitted?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            // Setting Height in the constructor fires this before the children
            // are built, so bail out until they are there.
            if (_text == null || _down == null || _up == null || _zero == null) return;

            const int key = 20;
            int h = Height;
            int textWidth = Math.Max(30, Width - key * 3 - 3);
            _text.SetBounds(0, 0, textWidth, h);
            _down.SetBounds(textWidth + 1, 0, key, h);
            _up.SetBounds(textWidth + 1 + key, 0, key, h);
            _zero.SetBounds(textWidth + 2 + key * 2, 0, key, h);
        }
    }
}
