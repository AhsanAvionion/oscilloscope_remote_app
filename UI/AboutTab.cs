using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ScopeControl.UI
{
    /// <summary>
    /// About page: who wrote it, what version, and what it talks to.
    /// </summary>
    public sealed class AboutTab : UserControl
    {
        public const string ProductName = "ScopeControl";
        public const string Version = "8.0";
        public const string ReleaseDate = "21 August 2026";
        public const string Developer = "Ahsan Mehmood";
        public const string Email = "ahsan.mehmood@outlook.com";

        public AboutTab()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Theme.Chassis;
            AutoScroll = true;

            var title = new Label
            {
                Text = ProductName,
                Location = new Point(24, 20),
                Size = new Size(400, 30),
                ForeColor = Theme.Accent,
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold)
            };

            var tagline = new Label
            {
                Text = "Remote control for Keysight InfiniiVision oscilloscopes",
                Location = new Point(26, 52),
                Size = new Size(460, 20),
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI", 9.5f)
            };

            var version = new Label
            {
                Text = "Version " + Version + "   ·   " + ReleaseDate,
                Location = new Point(26, 78),
                Size = new Size(400, 20),
                ForeColor = Theme.TextDim,
                Font = Theme.Mono
            };

            var rule = new Panel
            {
                Location = new Point(26, 106),
                Size = new Size(430, 1),
                BackColor = Theme.Border
            };

            var developedBy = new Label
            {
                Text = "Developed by",
                Location = new Point(26, 120),
                Size = new Size(200, 16),
                ForeColor = Theme.TextDim,
                Font = Theme.Header
            };

            var developer = new Label
            {
                Text = Developer,
                Location = new Point(26, 140),
                Size = new Size(400, 24),
                ForeColor = Theme.Text,
                Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold)
            };

            // A mail link rather than plain text: the address is the one thing
            // on this page anyone actually needs to act on.
            var email = new LinkLabel
            {
                Text = Email,
                Location = new Point(26, 166),
                Size = new Size(400, 20),
                LinkColor = Theme.Accent,
                ActiveLinkColor = Color.White,
                VisitedLinkColor = Theme.Accent,
                LinkBehavior = LinkBehavior.HoverUnderline,
                BackColor = Theme.Chassis,
                Font = Theme.Mono
            };
            email.LinkClicked += (s, e) =>
            {
                try { Process.Start("mailto:" + Email); }
                catch (Exception) { /* no mail client configured; the text is still readable */ }
            };

            var copyright = new Label
            {
                Text = "© " + DateTime.Now.Year + " " + Developer + ". All rights reserved.",
                Location = new Point(26, 198),
                Size = new Size(430, 20),
                ForeColor = Theme.TextDim,
                Font = Theme.Ui
            };

            var buildInfo = new Label
            {
                Text = "Runtime: .NET Framework 4.8   ·   Process: " +
                       (IntPtr.Size == 8 ? "64-bit" : "32-bit"),
                Location = new Point(26, 226),
                Size = new Size(430, 20),
                ForeColor = Theme.TextDim,
                Font = Theme.Mono
            };

            // ---- what it works with, kept factual
            var supportTitle = new Label
            {
                Text = "TESTED WITH",
                Location = new Point(520, 120),
                Size = new Size(200, 16),
                ForeColor = Theme.Accent,
                Font = Theme.Header
            };

            var support = new Label
            {
                Text =
                    "MSO-X 3024G   firmware 07.60\r\n" +
                    "MSO-X 2024A   firmware 02.65\r\n\r\n" +
                    "Other InfiniiVision 2000/3000/4000 X-Series models share the\r\n" +
                    "same command set and should work, but are untested.",
                Location = new Point(520, 140),
                Size = new Size(430, 90),
                ForeColor = Theme.Text,
                Font = Theme.Ui
            };

            var transports = new Label
            {
                Text = "Connects over LAN, USB (USBTMC) and serial, through VISA.NET,\r\n" +
                       "VISA-COM or a raw socket on port 5025.",
                Location = new Point(520, 236),
                Size = new Size(430, 40),
                ForeColor = Theme.TextDim,
                Font = Theme.Ui
            };

            Controls.AddRange(new Control[]
            {
                title, tagline, version, rule, developedBy, developer, email,
                copyright, buildInfo, supportTitle, support, transports
            });
        }
    }
}
