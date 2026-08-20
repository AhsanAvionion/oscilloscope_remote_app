using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ScopeControl
{
    /// <summary>
    /// What the user chose last time, kept in a plain key=value file under
    /// %APPDATA%\ScopeControl. Deliberately not the .NET settings system: this
    /// stays readable and editable by hand, and a corrupt file falls back to
    /// defaults rather than throwing at startup.
    /// </summary>
    public sealed class AppSettings
    {
        private const int MaxRecentAddresses = 8;

        public string Address = "TCPIP0::10.10.0.222::INSTR";
        public string Transport = "SOCKET";
        public int RefreshIntervalMs = 500;
        public bool AutoRefresh = true;
        public bool InkSaver;
        public bool CheckErrors = true;
        public bool ShowConsole;
        public bool ShowTopBar = true;
        public bool FollowPanel = true;
        public int SyncIntervalMs = 5000;
        public int WindowWidth;              // 0 means "use the design size"
        public int WindowHeight;
        public bool Maximized;
        public List<string> RecentAddresses = new List<string>();

        public static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ScopeControl");
                return Path.Combine(dir, "settings.txt");
            }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            try
            {
                if (!File.Exists(FilePath)) return settings;

                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string text = line.Trim();
                    if (text.Length == 0 || text.StartsWith("#")) continue;

                    int split = text.IndexOf('=');
                    if (split <= 0) continue;

                    string key = text.Substring(0, split).Trim();
                    string value = text.Substring(split + 1).Trim();

                    switch (key)
                    {
                        case "address": settings.Address = value; break;
                        case "transport": settings.Transport = value; break;
                        case "refreshMs": settings.RefreshIntervalMs = ParseInt(value, 500); break;
                        case "autoRefresh": settings.AutoRefresh = value == "1"; break;
                        case "inkSaver": settings.InkSaver = value == "1"; break;
                        case "checkErrors": settings.CheckErrors = value == "1"; break;
                        case "showConsole": settings.ShowConsole = value == "1"; break;
                        case "showTopBar": settings.ShowTopBar = value == "1"; break;
                        case "followPanel": settings.FollowPanel = value == "1"; break;
                        case "syncMs": settings.SyncIntervalMs = ParseInt(value, 5000); break;
                        case "windowWidth": settings.WindowWidth = ParseInt(value, 0); break;
                        case "windowHeight": settings.WindowHeight = ParseInt(value, 0); break;
                        case "maximized": settings.Maximized = value == "1"; break;
                        case "recent":
                            foreach (string entry in value.Split('|'))
                                if (entry.Trim().Length > 0) settings.RecentAddresses.Add(entry.Trim());
                            break;
                    }
                }
            }
            catch (Exception)
            {
                return new AppSettings();       // unreadable file, start clean
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                RememberAddress(Address);

                string path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));

                var text = new StringBuilder();
                text.AppendLine("# ScopeControl settings. Delete this file to start over.");
                text.AppendLine("address=" + Address);
                text.AppendLine("transport=" + Transport);
                text.AppendLine("refreshMs=" + RefreshIntervalMs.ToString(CultureInfo.InvariantCulture));
                text.AppendLine("autoRefresh=" + (AutoRefresh ? "1" : "0"));
                text.AppendLine("inkSaver=" + (InkSaver ? "1" : "0"));
                text.AppendLine("checkErrors=" + (CheckErrors ? "1" : "0"));
                text.AppendLine("showConsole=" + (ShowConsole ? "1" : "0"));
                text.AppendLine("showTopBar=" + (ShowTopBar ? "1" : "0"));
                text.AppendLine("followPanel=" + (FollowPanel ? "1" : "0"));
                text.AppendLine("syncMs=" + SyncIntervalMs.ToString(CultureInfo.InvariantCulture));
                text.AppendLine("windowWidth=" + WindowWidth.ToString(CultureInfo.InvariantCulture));
                text.AppendLine("windowHeight=" + WindowHeight.ToString(CultureInfo.InvariantCulture));
                text.AppendLine("maximized=" + (Maximized ? "1" : "0"));
                text.AppendLine("recent=" + string.Join("|", RecentAddresses.ToArray()));

                File.WriteAllText(path, text.ToString());
            }
            catch (Exception)
            {
                // A read-only profile is not worth interrupting the user over.
            }
        }

        /// <summary>Moves an address to the front of the recent list.</summary>
        public void RememberAddress(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return;
            address = address.Trim();

            Address = address;
            RecentAddresses.RemoveAll(
                a => string.Equals(a, address, StringComparison.OrdinalIgnoreCase));
            RecentAddresses.Insert(0, address);

            while (RecentAddresses.Count > MaxRecentAddresses)
                RecentAddresses.RemoveAt(RecentAddresses.Count - 1);
        }

        private static int ParseInt(string text, int fallback)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value : fallback;
        }
    }
}
