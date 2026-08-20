using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ScopeControl.UI
{
    /// <summary>
    /// The display area. Shows the captured instrument screen, and until one
    /// arrives it draws the 10 x 8 graticule so the window still reads as a scope.
    /// </summary>
    public sealed class ScopeScreen : Control
    {
        private Image _image;
        private string _message = "Not connected";

        public ScopeScreen()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
        }

        public string Message
        {
            get => _message;
            set { _message = value; Invalidate(); }
        }

        public Image Image
        {
            get => _image;
            set
            {
                Image old = _image;
                _image = value;
                Invalidate();
                if (old != null && !ReferenceEquals(old, value)) old.Dispose();
            }
        }

        public void ClearImage() => Image = null;

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.Black);

            if (_image != null)
            {
                DrawFitted(g, _image);
                return;
            }

            DrawGraticule(g);

            using (var font = new Font("Segoe UI", 11f))
            using (var brush = new SolidBrush(Color.FromArgb(120, 128, 136)))
            {
                var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString(_message, font, brush, ClientRectangle, format);
            }
        }

        private void DrawFitted(Graphics g, Image image)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            double scale = Math.Min((double)Width / image.Width, (double)Height / image.Height);
            int w = (int)(image.Width * scale);
            int h = (int)(image.Height * scale);
            g.DrawImage(image, (Width - w) / 2, (Height - h) / 2, w, h);
        }

        private void DrawGraticule(Graphics g)
        {
            int w = Width, h = Height;
            if (w < 20 || h < 20) return;

            using (var dim = new Pen(Theme.Graticule))
            using (var bright = new Pen(Theme.GraticuleBright))
            {
                for (int i = 1; i < 10; i++)
                {
                    int x = w * i / 10;
                    g.DrawLine(i == 5 ? bright : dim, x, 0, x, h);
                }
                for (int i = 1; i < 8; i++)
                {
                    int y = h * i / 8;
                    g.DrawLine(i == 4 ? bright : dim, 0, y, w, y);
                }
                g.DrawRectangle(bright, 0, 0, w - 1, h - 1);
            }
        }
    }
}
