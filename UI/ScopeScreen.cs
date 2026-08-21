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
        private bool _showGuide;
        private bool _armed;

        public ScopeScreen()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);

            // Clicking here must not take focus: the whole point is that the
            // cursor field being edited stays focused so the click can target it.
            SetStyle(ControlStyles.Selectable, false);
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

        // ------------------------------------------------------- click to place

        /// <summary>
        /// Where the graticule sits inside the captured image, as fractions of
        /// its width and height. This cannot be assumed: the waveform area is a
        /// different part of the picture on each model and firmware, so the
        /// user aligns it once with the guide switched on.
        /// </summary>
        public RectangleF GraticuleFractions { get; set; } = new RectangleF(0.075f, 0.055f, 0.905f, 0.80f);

        /// <summary>Draws the graticule outline so it can be lined up by eye.</summary>
        public bool ShowGuide
        {
            get => _showGuide;
            set { _showGuide = value; Invalidate(); }
        }

        /// <summary>True while a click should place a cursor rather than do nothing.</summary>
        public bool Armed
        {
            get => _armed;
            set
            {
                _armed = value;
                Cursor = value ? Cursors.Cross : Cursors.Default;
            }
        }

        /// <summary>
        /// Raised with the click position as fractions of the graticule: 0,0 is
        /// its top left corner and 1,1 the bottom right. Values outside 0..1 mean
        /// the click landed off the graticule and are not reported.
        /// </summary>
        public event Action<double, double> GraticuleClicked;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!_armed || _image == null) return;

            Rectangle drawn = DrawnImageBounds();
            if (drawn.Width <= 0 || drawn.Height <= 0) return;
            if (!drawn.Contains(e.Location)) return;

            // Client point -> fraction of the image -> fraction of the graticule.
            double imageX = (e.X - drawn.X) / (double)drawn.Width;
            double imageY = (e.Y - drawn.Y) / (double)drawn.Height;

            RectangleF g = GraticuleFractions;
            if (g.Width <= 0 || g.Height <= 0) return;

            double x = (imageX - g.Left) / g.Width;
            double y = (imageY - g.Top) / g.Height;

            if (x < 0 || x > 1 || y < 0 || y > 1) return;
            GraticuleClicked?.Invoke(x, y);
        }

        /// <summary>Where the image is actually painted, after letterboxing.</summary>
        private Rectangle DrawnImageBounds()
        {
            if (_image == null) return Rectangle.Empty;
            double scale = Math.Min((double)Width / _image.Width, (double)Height / _image.Height);
            int w = (int)(_image.Width * scale);
            int h = (int)(_image.Height * scale);
            return new Rectangle((Width - w) / 2, (Height - h) / 2, w, h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.Black);

            if (_image != null)
            {
                DrawFitted(g, _image);
                if (_showGuide) DrawGuide(g);
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

        /// <summary>
        /// Outlines where we think the graticule is, with division marks, so it
        /// can be nudged until it matches the real one underneath.
        /// </summary>
        private void DrawGuide(Graphics g)
        {
            Rectangle drawn = DrawnImageBounds();
            if (drawn.Width <= 0) return;

            RectangleF f = GraticuleFractions;
            var box = new Rectangle(
                drawn.X + (int)(f.Left * drawn.Width),
                drawn.Y + (int)(f.Top * drawn.Height),
                (int)(f.Width * drawn.Width),
                (int)(f.Height * drawn.Height));

            using (var pen = new Pen(Color.FromArgb(220, 255, 64, 129), 1.5f))
            {
                g.DrawRectangle(pen, box);
                for (int i = 1; i < 10; i++)
                {
                    int x = box.X + box.Width * i / 10;
                    g.DrawLine(pen, x, box.Y, x, box.Y + 5);
                    g.DrawLine(pen, x, box.Bottom - 5, x, box.Bottom);
                }
                for (int i = 1; i < 8; i++)
                {
                    int y = box.Y + box.Height * i / 8;
                    g.DrawLine(pen, box.X, y, box.X + 5, y);
                    g.DrawLine(pen, box.Right - 5, y, box.Right, y);
                }
            }

            using (var font = new Font("Segoe UI", 8f))
            using (var brush = new SolidBrush(Color.FromArgb(255, 64, 129)))
                g.DrawString("Align this box with the graticule", font, brush, box.X + 4, box.Y + 4);
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
