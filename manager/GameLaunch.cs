// SPDX-License-Identifier: GPL-3.0-only
// How the Launch button starts the game.
//
// Separate from the window because it is the only part of launching that can be
// wrong in an interesting way, and a file the window owns cannot be tested: the
// test build is compiled with the in-box csc and has no WPF.
//
// The bug this exists for: starting DyingLightGame_TheBeast_x64_rwdi.exe
// directly gets as far as a dialog reading "Steam client application is required
// in order to play the game" and no further. The executable is DRM-wrapped and
// expects to have been started by Steam, which sets up an environment a plain
// CreateProcess does not. The old code set the working directory carefully,
// which was a reasonable thing to be careful about and not the problem.

using System;
using System.IO;

namespace CraneManager
{
    public static class GameLaunch
    {
        public const string Executable = "DyingLightGame_TheBeast_x64_rwdi.exe";

        // Dying Light: The Beast. A global Steam id, the same on every install;
        // read from steamapps\appmanifest_3008130.acf rather than guessed.
        public const string SteamAppId = "3008130";

        public static string SteamUri
        {
            get { return "steam://rungameid/" + SteamAppId; }
        }

        /*
         * How to start the game. Three answers, and the middle one is the good
         * one.
         *
         * SteamUri hands the job to Steam. It works, and it has two costs: Steam
         * shows its own launch progress, and Steam lets the DRM stub restart the
         * process -- which loads the whole ASI stack twice, giving two Bridge
         * consoles, one of them inert. That second console is a second copy of
         * every client, racing the first for the same engine state.
         *
         * DirectWithSteamEnv is what the Vortex extension does, and it is better
         * on both counts. It runs the exe with SteamAppId set in the child's
         * environment, which is the documented Steamworks route for starting a
         * Steam build outside Steam: SteamAPI_Init binds to the running client,
         * SteamAPI_RestartAppIfNecessary returns false, and nothing relaunches.
         * One process, one console, no splash. It requires the Steam client to
         * already be running -- without it the game shows "Steam client
         * application is required in order to play the game", which is the
         * failure that started all this.
         *
         * Direct is for a copy that is not a Steam install at all.
         */
        public enum Route { Direct, DirectWithSteamEnv, SteamUri }

        // Split from the doing so it can be tested; starting a game cannot be.
        public static Route Plan(string folder, bool steamClientRunning)
        {
            if (!IsSteamInstall(folder)) return Route.Direct;
            // Falling back to the URI rather than refusing: it starts Steam and
            // then the game, so the button still works from a cold machine. It
            // costs the splash and the duplicate console, which is a fair price
            // for not making the user go and start Steam themselves.
            return steamClientRunning ? Route.DirectWithSteamEnv : Route.SteamUri;
        }

        // Steamworks reads both; on a plain app they carry the same value.
        // Windows environment names are case-insensitive, so the exact casing
        // here does not matter -- matching Steamworks' documented spelling does.
        public static void ApplySteamEnvironment(System.Collections.Specialized.StringDictionary env)
        {
            env["SteamAppId"] = SteamAppId;
            env["SteamGameId"] = SteamAppId;
        }

        /*
         * Whether the game is running, asked directly.
         *
         * "Game: connected" used to be inferred purely from how recently Crane
         * had written its status file, which is sound reasoning and a bad
         * indicator: a report five seconds old and a game that exited four
         * minutes ago look identical, so the header went on claiming a
         * connection long after the game was gone. Asking the process list
         * answers it outright.
         *
         * The process name carries no extension, matching Process.ProcessName.
         */
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

        /*
         * Whether this install sits under a Steam library.
         *
         * Detected by walking up for a "steamapps" directory rather than by
         * looking for Steam itself: what matters is how this copy was installed,
         * not whether Steam exists on the machine. Somebody with Steam installed
         * and a non-Steam copy of the game must still get a direct launch,
         * because steam://rungameid on a copy Steam does not know about fails
         * silently -- no error, no game, nothing to tell the user what happened.
         */
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
