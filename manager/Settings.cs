using System;
using System.IO;
using System.Text;

namespace CraneManager
{
    internal static class Settings
    {
        private static string PathToFile()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DLTB-CraneManager");
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

        public static void SaveFolder(string folder)
        {
            try { File.WriteAllText(PathToFile(), folder, new UTF8Encoding(false)); }
            catch (IOException) { }
        }
    }
}
