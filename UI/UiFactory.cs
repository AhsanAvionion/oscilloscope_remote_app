using System;
using System.Drawing;
using System.Windows.Forms;
using ScopeControl.Instrument;

namespace ScopeControl.UI
{
    /// <summary>An item that shows one thing and sends another to the instrument.</summary>
    public sealed class Choice
    {
        public string Text;
        public string Scpi;
        public double Value;

        /// <summary>Set on entries added to hold a value read back from the
        /// instrument that is not on the standard 1-2-5 list.</summary>
        public bool IsReadback;

        public Choice(string text, string scpi) { Text = text; Scpi = scpi; }
        public Choice(string text, double value) { Text = text; Value = value; Scpi = text; }

        public override string ToString() => Text;
    }

    public static class UiFactory
    {
        public static Label Caption(string text, int x, int y, int width = 62)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y + 4),
                Size = new Size(width, 16),
                ForeColor = Theme.TextDim,
                Font = Theme.Ui,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };
        }

        public static Label Value(string text, int x, int y, int width, Color color)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 18),
                ForeColor = color,
                Font = Theme.Readout,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoSize = false
            };
        }

        public static Button Key(string text, int x, int y, int width, int height)
        {
            var b = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, height),
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Key,
                ForeColor = Theme.Text,
                Font = Theme.KeyFont,
                UseVisualStyleBackColor = false,
                TabStop = false
            };
            b.FlatAppearance.BorderColor = Theme.Border;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Theme.KeyHover;
            return b;
        }

        /// <summary>Repaints a key to show whether it is the active choice.</summary>
        public static void SetKeyActive(Button key, bool active, Color activeColor)
        {
            key.BackColor = active ? activeColor : Theme.Key;
            key.ForeColor = active ? PickReadableText(activeColor) : Theme.Text;
            key.FlatAppearance.BorderColor = active ? activeColor : Theme.Border;
        }

        public static Color PickReadableText(Color background)
        {
            double luma = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
            return luma > 0.6 ? Color.FromArgb(16, 17, 19) : Color.White;
        }

        public static ComboBox Combo(int x, int y, int width)
        {
            var c = new ComboBox
            {
                Location = new Point(x, y),
                Size = new Size(width, 22),
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = Theme.Field,
                ForeColor = Theme.Text,
                Font = Theme.Ui,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = 16,
                TabStop = false
            };
            c.DrawItem += DrawComboItem;
            return c;
        }

        private static void DrawComboItem(object sender, DrawItemEventArgs e)
        {
            var combo = (ComboBox)sender;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (var brush = new SolidBrush(selected ? Theme.Accent : Theme.Field))
                e.Graphics.FillRectangle(brush, e.Bounds);

            if (e.Index >= 0 && e.Index < combo.Items.Count)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    combo.Items[e.Index].ToString(),
                    combo.Font,
                    new Rectangle(e.Bounds.X + 3, e.Bounds.Y, e.Bounds.Width - 3, e.Bounds.Height),
                    selected ? UiFactory.PickReadableText(Theme.Accent) : Theme.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            }
        }

        public static CheckBox Check(string text, int x, int y, int width)
        {
            // DarkCheckBox, not CheckBox: the stock tick is drawn from the
            // Windows theme and is barely visible on this palette.
            return new DarkCheckBox
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 20)
            };
        }

        public static TextBox Field(int x, int y, int width)
        {
            return new TextBox
            {
                Location = new Point(x, y),
                Size = new Size(width, 22),
                BackColor = Theme.Field,
                ForeColor = Theme.Text,
                Font = Theme.Mono,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        /// <summary>A titled box, drawn with a hairline rule rather than a 3D groupbox.</summary>
        public static Panel Group(string title, int x, int y, int width, int height)
        {
            var panel = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, height),
                BackColor = Theme.Group,
                Padding = new Padding(0)
            };
            panel.Paint += (s, e) =>
            {
                var p = (Panel)s;
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawRectangle(pen, 0, 0, p.Width - 1, p.Height - 1);
                using (var pen = new Pen(Theme.Accent, 2))
                    e.Graphics.DrawLine(pen, 1, 1, 1, 22);
                TextRenderer.DrawText(e.Graphics, title, Theme.Header,
                    new Point(10, 6), Theme.Accent);
                using (var pen = new Pen(Theme.Border))
                    e.Graphics.DrawLine(pen, 8, 24, p.Width - 9, 24);
            };
            return panel;
        }

        public static void FillCombo(ComboBox combo, params Choice[] choices)
        {
            combo.Items.Clear();
            foreach (var c in choices) combo.Items.Add(c);
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        /// <summary>Selects by exact Scpi value. Use for app-level choices such as
        /// the transport, where prefix matching would confuse VISA with VISACOM.</summary>
        public static void SelectExactScpi(ComboBox combo, string value)
        {
            if (string.IsNullOrEmpty(value)) return;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (string.Equals(((Choice)combo.Items[i]).Scpi, value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>Selects the item whose Value matches exactly.</summary>
        public static void SelectExactValue(ComboBox combo, double value)
        {
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (Math.Abs(((Choice)combo.Items[i]).Value - value) < 1e-9)
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>Selects the item whose Scpi field matches, ignoring the usual SCPI abbreviations.</summary>
        public static void SelectScpi(ComboBox combo, string reply)
        {
            if (string.IsNullOrWhiteSpace(reply)) return;
            string want = reply.Trim().ToUpperInvariant();
            for (int i = 0; i < combo.Items.Count; i++)
            {
                string have = ((Choice)combo.Items[i]).Scpi.ToUpperInvariant();
                string haveShort = ShortForm(have);
                if (have == want || haveShort == want ||
                    want.StartsWith(haveShort) || haveShort.StartsWith(want))
                {
                    combo.SelectedIndex = i;
                    return;
                }
            }
        }

        /// <summary>":CHANnel" style keywords carry their short form in the capitals.</summary>
        private static string ShortForm(string keyword)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in keyword) if (char.IsUpper(c) || char.IsDigit(c)) sb.Append(c);
            return sb.Length == 0 ? keyword : sb.ToString();
        }

        /// <summary>
        /// Selects the entry matching a value read back from the instrument.
        /// The scope can sit on values the 1-2-5 list does not contain - 420 ns/div
        /// after an autoscale, say - so rather than snapping to the nearest and
        /// showing something untrue, the exact value is inserted and selected.
        /// </summary>
        public static void SelectOrInsertValue(ComboBox combo, double value, string unit)
        {
            if (value <= 0) return;

            // Drop the entry inserted last time; only one readback value is kept.
            for (int i = combo.Items.Count - 1; i >= 0; i--)
            {
                var existing = combo.Items[i] as Choice;
                if (existing != null && existing.IsReadback) combo.Items.RemoveAt(i);
            }

            for (int i = 0; i < combo.Items.Count; i++)
            {
                var candidate = (Choice)combo.Items[i];
                if (candidate.Value <= 0) continue;
                if (Math.Abs(candidate.Value - value) <= Math.Abs(value) * 0.01)
                {
                    combo.SelectedIndex = i;      // close enough to a standard step
                    return;
                }
            }

            int index = 0;
            while (index < combo.Items.Count && ((Choice)combo.Items[index]).Value < value) index++;
            combo.Items.Insert(index, new Choice(Eng.Format(value, unit), value) { IsReadback = true });
            combo.SelectedIndex = index;
        }

        public static void SelectValue(ComboBox combo, double value)
        {
            int best = -1;
            double bestErr = double.MaxValue;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                double v = ((Choice)combo.Items[i]).Value;
                if (v <= 0) continue;
                double err = Math.Abs(Math.Log10(v) - Math.Log10(Math.Abs(value) < 1e-18 ? 1e-18 : Math.Abs(value)));
                if (err < bestErr) { bestErr = err; best = i; }
            }
            if (best >= 0) combo.SelectedIndex = best;
        }
    }
}
