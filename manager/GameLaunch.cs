using System;
using System.IO;

namespace CraneManager
{
    public static class GameLaunch
    {
        public const string Executable = "DyingLightGame_TheBeast_x64_rwdi.exe";

        public const string SteamAppId = "3008130";

        public static string SteamUri
        {
            get { return "steam://rungameid/" + SteamAppId; }
        }

        public enum Route { Direct, DirectWithSteamEnv, SteamUri }

        public static Route Plan(string folder, bool steamClientRunning)
        {
            if (!IsSteamInstall(folder)) return Route.Direct;

            return steamClientRunning ? Route.DirectWithSteamEnv : Route.SteamUri;
        }

        public static void ApplySteamEnvironment(System.Collections.Specialized.StringDictionary env)
        {
            env["SteamAppId"] = SteamAppId;
            env["SteamGameId"] = SteamAppId;
        }

        public const string ProcessName = "DyingLightGame_TheBeast_x64_rwdi";

        public static bool IsGameRunning()
        {
            try
            {
                return System.Diagnostics.Process.GetProcessesByName(ProcessName).Length > 0;
            }
            catch (Exception) { return false; }
        }

        public static bool IsSteamClientRunning()
        {
            try
            {
                return System.Diagnostics.Process.GetProcessesByName("steam").Length > 0;
            }
            catch (Exception) { return false; }
        }

        public static bool IsSteamInstall(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            try
            {
                DirectoryInfo dir = new DirectoryInfo(folder);
                while (dir != null)
                {
                    if (string.Equals(dir.Name, "steamapps", StringComparison.OrdinalIgnoreCase))
                        return true;
                    dir = dir.Parent;
                }
            }
            catch (ArgumentException) { }
            catch (PathTooLongException) { }
            catch (NotSupportedException) { }
            return false;
        }
    }
}
