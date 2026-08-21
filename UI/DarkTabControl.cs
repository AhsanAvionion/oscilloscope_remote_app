using System.Drawing;
using System.Windows.Forms;

namespace ScopeControl.UI
{
    /// <summary>
    /// TabControl paints its tabs from the Windows theme and ignores BackColor,
    /// which looks wrong against a dark instrument panel. Owner drawing the tab
    /// headers is the only way to control them.
    /// </summary>
    public sealed class DarkTabControl : TabControl
    {
        public DarkTabControl()
        {
            DrawMode = TabDrawMode.OwnerDrawFixed;
            SizeMode = TabSizeMode.Fixed;
            ItemSize = new Size(120, 24);
            Appearance = TabAppearance.Normal;
            Font = Theme.Ui;
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            bool selected = e.Index == SelectedIndex;
            Rectangle bounds = e.Bounds;

            using (var back = new SolidBrush(selected ? Theme.Group : Theme.Chassis))
                e.Graphics.FillRectangle(back, bounds);

            if (selected)
            {
                using (var accent = new SolidBrush(Theme.Accent))
                    e.Graphics.FillRectangle(accent, bounds.X, bounds.Y, bounds.Width, 2);
            }

            TextRenderer.DrawText(
                e.Graphics,
                TabPages[e.Index].Text,
                selected ? Theme.UiBold : Theme.Ui,
                bounds,
                selected ? Theme.Text : Theme.TextDim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            using (var brush = new SolidBrush(Theme.Chassis))
                e.Graphics.FillRectangle(brush, ClientRectangle);
        }
    }
}
