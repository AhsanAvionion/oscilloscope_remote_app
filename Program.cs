using System;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ScopeControl.UI;

namespace ScopeControl
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
#if !NETFRAMEWORK
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
#endif
            // On .NET Framework, DPI awareness comes from App.config instead.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Anything thrown before the message loop starts would otherwise
            // kill the process with no window and no message at all.
            AppDomain.CurrentDomain.UnhandledException +=
                (s, e) => ReportFatal(e.ExceptionObject as Exception, "Unhandled exception");
            Application.ThreadException +=
                (s, e) => ReportFatal(e.Exception, "Unhandled UI exception");

            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                ReportFatal(ex, "Startup failed");
            }
        }

        private static void ReportFatal(Exception ex, string title)
        {
            string detail = ex == null ? "No exception detail available." : ex.ToString();
            string logPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "ScopeControl-error.log");

            try
            {
                File.AppendAllText(logPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + title +
                    Environment.NewLine + detail + Environment.NewLine + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch (Exception)
            {
                logPath = "(could not write a log file)";
            }

            MessageBox.Show(
                detail + Environment.NewLine + Environment.NewLine + "Written to: " + logPath,
                title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
