using System;
using System.Drawing;
using System.Windows.Forms;
using ScopeControl.Instrument;

namespace ScopeControl.UI
{
    /// <summary>
    /// One line of live readouts under the display: a channel and three
    /// measurements of it. Several stack so more than one channel can be
    /// watched at once.
    /// </summary>
    public sealed class ReadoutRow : UserControl
    {
        public const int Slots = 3;

        private const int DesignWidth = 764;
        private const int DesignHeight = 24;

        private readonly ComboBox _channel;
        private readonly ComboBox[] _pickers = new ComboBox[Slots];
        private readonly Label[] _values = new Label[Slots];

        public ReadoutRow(int channelNumber, params string[] keywords)
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Theme.Bezel;
            // A UserControl defaults to 150x150. Without a real width the later
            // columns sit outside the control and never get painted.
            Size = new Size(DesignWidth, DesignHeight);

            _channel = UiFactory.Combo(4, 1, 76);
            for (int i = 1; i <= 4; i++) _channel.Items.Add(new Choice("CH " + i, "CHANnel" + i));
            _channel.Items.Add(new Choice("Math", "FUNCtion"));
            _channel.SelectedIndex = Math.Min(Math.Max(channelNumber - 1, 0), 4);
            _channel.SelectedIndexChanged += (s, e) => { UpdateColours(); Raise(); };
            Controls.Add(_channel);

            for (int slot = 0; slot < Slots; slot++)
            {
                int x = 86 + slot * 224;

                var picker = UiFactory.Combo(x, 1, 112);
                picker.Items.Add(Measurements.None);
                foreach (var m in Measurements.All) picker.Items.Add(m);
                Select(picker, slot < keywords.Length ? keywords[slot] : "VPP");
                picker.SelectedIndexChanged += (s, e) => Raise();
                _pickers[slot] = picker;
                Controls.Add(picker);

                var value = UiFactory.Value("----", x + 116, 2, 104, Theme.Text);
                _values[slot] = value;
                Controls.Add(value);
            }

            UpdateColours();
        }

        /// <summary>Raised when the channel or any measurement changes.</summary>
        public event EventHandler SelectionChanged;

        private void Raise() => SelectionChanged?.Invoke(this, EventArgs.Empty);

        private void UpdateColours()
        {
            // Colour the numbers like the trace they belong to.
            int index = _channel.SelectedIndex;
            Color colour = index >= 0 && index < 4 ? Theme.Channel(index + 1) : Theme.Math;
            foreach (var label in _values) label.ForeColor = colour;
        }

        private static void Select(ComboBox combo, string keyword)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (((Measurement)combo.Items[i]).Keyword == keyword)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
            combo.SelectedIndex = 0;      // index 0 is "none"
        }

        public string Source =>
            _channel.SelectedItem == null ? "CHANnel1" : ((Choice)_channel.SelectedItem).Scpi;

        public Measurement MeasurementAt(int slot) => _pickers[slot].SelectedItem as Measurement;

        public void SetValue(int slot, string text) => _values[slot].Text = text;

        public void SetAll(string text)
        {
            foreach (var label in _values) label.Text = text;
        }

        public void SetInteractive(bool enabled)
        {
            _channel.Enabled = enabled;
            foreach (var picker in _pickers) picker.Enabled = enabled;
        }
    }
}
