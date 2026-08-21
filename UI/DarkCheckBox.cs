using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScopeControl.UI
{
    /// <summary>
    /// A checkbox drawn by hand.
    ///
    /// The stock control paints its tick from the Windows theme, in a colour
    /// chosen for a light background. On a dark panel it comes out nearly
    /// invisible, and no property changes it: ForeColor only affects the label.
    /// So the box and the tick are drawn here instead.
    /// </summary>
    public sealed class DarkCheckBox : CheckBox
    {
        private bool _hot;

        public DarkCheckBox()
        {
            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            // Opaque, not transparent. A transparent owner-painted control asks
            // its parent to redraw the background underneath it, and these sit
            // on panels that paint their own titles and rules - so the old text
            // came back through and every repaint smeared over the last.
            BackColor = Theme.Group;
            ForeColor = Theme.Text;
            Font = Theme.Ui;
            FlatStyle = FlatStyle.Flat;
            TabStop = false;
        }

        protected override void OnParentChanged(System.EventArgs e)
        {
            base.OnParentChanged(e);
            // Match whatever we are sitting on, so the fill is invisible.
            if (Parent != null) BackColor = Parent.BackColor;
            Invalidate();
        }

        protected override void OnMouseEnter(System.EventArgs e)
        {
            base.OnMouseEnter(e);
            _hot = true;
            Invalidate();
        }

        protected override void OnMouseLeave(System.EventArgs e)
        {
            base.OnMouseLeave(e);
            _hot = false;
            Invalidate();
        }

        protected override void OnCheckedChanged(System.EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            // Clear first: nothing else paints this area now.
            using (var background = new SolidBrush(BackColor))
                g.FillRectangle(background, ClientRectangle);

            g.SmoothingMode = SmoothingMode.AntiAlias;

            int side = Height - 6;
            if (side > 16) side = 16;
            if (side < 11) side = 11;
            int top = (Height - side) / 2;
            var box = new Rectangle(1, top, side, side);

            Color fill = Checked ? Theme.Accent : Theme.Field;
            Color edge = !Enabled ? Theme.Border
                : Checked ? Theme.Accent
                : _hot ? Theme.Accent
                : Theme.Border;

            if (!Enabled) fill = Checked ? Theme.Border : Theme.Field;

            using (var brush = new SolidBrush(fill))
                g.FillRectangle(brush, box);
            using (var pen = new Pen(edge))
                g.DrawRectangle(pen, box);

            if (Checked)
            {
                // A thick tick in a colour picked for contrast against the fill,
                // which is the whole reason this control exists.
                Color tick = Enabled ? UiFactory.PickReadableText(fill) : Theme.TextDim;
                using (var pen = new Pen(tick, 2.2f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                    LineJoin = LineJoin.Round
                })
                {
                    float left = box.Left + side * 0.22f;
                    float middle = box.Left + side * 0.42f;
                    float right = box.Left + side * 0.79f;
                    float high = box.Top + side * 0.28f;
                    float low = box.Top + side * 0.68f;
                    float mid = box.Top + side * 0.50f;

                    g.DrawLines(pen, new[]
                    {
                        new PointF(left, mid),
                        new PointF(middle, low),
                        new PointF(right, high)
                    });
                }
            }

            var textArea = new Rectangle(box.Right + 6, 0, Width - box.Right - 6, Height);
            TextRenderer.DrawText(g, Text, Font, textArea,
                Enabled ? ForeColor : Theme.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
