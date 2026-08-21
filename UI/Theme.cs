using System.Drawing;

namespace ScopeControl.UI
{
    /// <summary>
    /// Palette lifted from the instrument itself: charcoal chassis, softkey grey,
    /// and the four InfiniiVision trace colours.
    /// </summary>
    public static class Theme
    {
        public static readonly Color Chassis = Color.FromArgb(24, 25, 28);
        public static readonly Color Bezel = Color.FromArgb(38, 40, 45);
        public static readonly Color Group = Color.FromArgb(32, 34, 38);
        public static readonly Color Field = Color.FromArgb(20, 21, 24);
        public static readonly Color Border = Color.FromArgb(58, 61, 68);
        public static readonly Color Key = Color.FromArgb(52, 55, 61);
        public static readonly Color KeyHover = Color.FromArgb(70, 74, 82);

        public static readonly Color Text = Color.FromArgb(228, 230, 234);
        public static readonly Color TextDim = Color.FromArgb(146, 152, 161);
        public static readonly Color Accent = Color.FromArgb(0, 169, 224);

        public static readonly Color Run = Color.FromArgb(46, 170, 74);
        public static readonly Color Stop = Color.FromArgb(200, 58, 54);
        public static readonly Color Single = Color.FromArgb(224, 186, 60);

        public static readonly Color Graticule = Color.FromArgb(52, 56, 60);
        public static readonly Color GraticuleBright = Color.FromArgb(78, 84, 90);

        /// <summary>CH1 yellow, CH2 green, CH3 blue, CH4 magenta.</summary>
        private static readonly Color[] ChannelColors =
        {
            Color.FromArgb(255, 214, 0),
            Color.FromArgb(60, 207, 78),
            Color.FromArgb(64, 169, 243),
            Color.FromArgb(226, 85, 196)
        };

        public static Color Channel(int number) => ChannelColors[(number - 1) % 4];

        /// <summary>The math waveform, purple as on the instrument.</summary>
        public static readonly Color Math = Color.FromArgb(176, 132, 255);

        public static readonly Font Ui = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        public static readonly Font UiBold = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        public static readonly Font Header = new Font("Segoe UI Semibold", 8f, FontStyle.Bold);
        public static readonly Font KeyFont = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
        public static readonly Font Mono = new Font("Consolas", 8.5f);
        public static readonly Font Readout = new Font("Consolas", 10f, FontStyle.Bold);
    }
}
