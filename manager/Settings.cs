// SPDX-License-Identifier: GPL-3.0-only
// Remembers which folder the manager was last pointed at.
//
// Its own file because it is shared: the WinForms window used it and the WPF
// window uses it. One value, in %APPDATA%, and it is only ever a fallback --
// the manager normally finds Crane beside itself.

using System;
using System.IO;
using System.Text;

namespace CraneLoader
{
    internal static class Settings
    {
        private static string PathToFile()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DLTB-CraneLoader");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "folder.txt");
        }

        public static string LoadFolder()
        {
            try
            {
                string file = PathToFile();
                if (!File.Exists(file)) return "";
                string folder = File.ReadAllText(file, Encoding.UTF8).Trim();
                return Directory.Exists(folder) ? folder : "";
            }
            catch (IOException) { return ""; }
        }

        // UI scale, persisted beside the folder. Stored as a percentage so the
        // file stays readable if anybody opens it.
        public static double LoadScale()
        {
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(PathToFile()), "scale.txt");
                if (!File.Exists(file)) return 1.0;
                double percent;
                if (!double.TryParse(File.ReadAllText(file).Trim(),
                                     System.Globalization.NumberStyles.Float,
                                     System.Globalization.CultureInfo.InvariantCulture,
                                     out percent)) return 1.0;
                if (percent < 75) percent = 75;
                if (percent > 200) percent = 200;
                return percent / 100.0;
            }
            catch (IOException) { return 1.0; }
        }

        public static void SaveScale(double scale)
        {
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(PathToFile()), "scale.txt");
                File.WriteAllText(file,
                    (scale * 100.0).ToString("0", System.Globalization.CultureInfo.InvariantCulture),
                    new UTF8Encoding(false));
            }
            catch (IOException) { }
        }

        // Theme, persisted by name rather than by its colours: the palettes may
        // be re-tuned between versions and a saved name picks the improvements
        // up, where saved hex values would pin somebody to an old draft.
        public static string LoadTheme()
        {
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(PathToFile()), "theme.txt");
                if (!File.Exists(file)) return Theme.Default;
                string name = File.ReadAllText(file, Encoding.UTF8).Trim();
                return name.Length == 0 ? Theme.Default : name;
            }
            catch (IOException) { return Theme.Default; }
        }

        public static void SaveTheme(string name)
        {
            try
            {
                string file = Path.Combine(Path.GetDirectoryName(PathToFile()), "theme.txt");
                File.WriteAllText(file, name, new UTF8Encoding(false));
            }
            catch (IOException) { }
        }

        public static void SaveFolder(string folder)
        {
            try { File.WriteAllText(PathToFile(), folder, new UTF8Encoding(false)); }
            catch (IOException) { }
        }
    }
}
