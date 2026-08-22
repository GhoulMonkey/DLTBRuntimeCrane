// SPDX-License-Identifier: GPL-3.0-only
// Reads DLTBRuntimeCrane.status.json -- the runtime's report back.
//
// The manager writes the manifest and Crane reads it. This is the only channel
// in the other direction, and without it the window can show that a script is
// ticked but never that it failed on line 47 -- a checkbox reading "enabled"
// beside a script that never loaded.
//
// Written by Crane after every reload. Read here on the FileSystemWatcher that
// already covers this directory.
//
// Tolerant where the manifest reader is strict, for a reason. A malformed
// manifest is the user's file and they must be told precisely what is wrong; a
// malformed status file is our bug, the user cannot fix it, and refusing to show
// the script list because a report was unreadable would punish them for it. So
// this returns what it could read and drops what it could not.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace CraneLoader
{
    public enum ScriptState { Unknown, Disabled, Loaded, Missing, Failed, Waiting }

    public class StatusReport
    {
        public bool WritesEnabled { get; set; }
        public Dictionary<string, ScriptState> States { get; set; }
        public Dictionary<string, string> Errors { get; set; }

        public StatusReport()
        {
            States = new Dictionary<string, ScriptState>(StringComparer.OrdinalIgnoreCase);
            Errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public ScriptState StateOf(string file)
        {
            ScriptState state;
            return States.TryGetValue(file, out state) ? state : ScriptState.Unknown;
        }

        public string ErrorOf(string file)
        {
            string error;
            return Errors.TryGetValue(file, out error) ? error : "";
        }
    }

    public static class StatusFile
    {
        public const string FileName = "DLTBRuntimeCrane.status.json";

        /*
         * How recently Crane must have written for its report to still be true.
         *
         * Crane rewrites this file on every reload and cannot write it at all
         * when the game is not running, so age is the only available evidence
         * that the report describes the present. This lives here, once, because
         * it was previously applied in only one of the two places that need it:
         * the header bar aged the file and said "Game: not running", while the
         * state dots read the same file with no age check and reported scripts
         * as loaded from a session that had ended half an hour earlier.
         *
         * The window therefore contradicted itself, and the half that was wrong
         * was the reassuring half -- a green dot beside a script that was not
         * running, which is the failure direction that actually costs somebody
         * a playtest.
         */
        public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(5);

        /*
         * Whether Crane's report describes the session in front of the user.
         *
         * The age window above was the wrong test and had to go, because the
         * premise under it was false: Crane does not rewrite this file while the
         * game runs. It writes it once per reload -- see write_status in
         * Crane.c, called only from reload_scripts -- so in an ordinary session
         * the file is written seconds after launch and never touched again.
         *
         * Judging it by age therefore does not decide whether the game is
         * running, it decides whether the user has been playing for less than
         * five minutes. Seven minutes in, with the game running, the scripts
         * loaded and the console printing, the window said GAME STARTING and the
         * per-script dots went blank. Nothing was wrong except the question.
         *
         * The real question is whether this report came from THIS run of the
         * game, and the process start time answers it exactly. A report written
         * after the process started is this session's, at any age. One written
         * before it is a leftover, however recent -- which is the case the age
         * window was there to catch, and this catches it strictly better: a
         * restart invalidates the old report immediately rather than five
         * minutes later.
         *
         * `gameStartedUtc` is null when the game is not running, or when its
         * start time could not be read. Those are different situations and the
         * caller has already distinguished them; this only has to handle the
         * second, which falls back to the age window.
         */
        public static bool DescribesCurrentSession(string folder, DateTime? gameStartedUtc)
        {
            return Judge(folder, true, gameStartedUtc).Current;
        }

        /*
         * The verdict and the reason for it, decided together.
         *
         * One method rather than a test plus a sentence describing the test,
         * because a sentence maintained separately from the rule it describes is
         * a sentence that will eventually describe the previous rule. That is
         * not hypothetical here: this indicator has now been wrong twice, the
         * 2026-08-18 fix unified two readers of the same file for exactly this
         * reason, and the surviving comment about the five-minute window went on
         * asserting a premise that was already false.
         *
         * The reason exists because both defects were found by the user from a
         * screenshot, and in both cases the screenshot showed the verdict and
         * nothing about how it was reached -- so establishing why cost a round
         * trip and, the first time, a wrong diagnosis. Hovering the capsule now
         * answers it outright.
         */
        public class SessionVerdict
        {
            // Whether Crane's report describes the session in front of the user.
            public bool Current;
            // Why, in the user's own terms. Shown as the capsule's tooltip.
            public string Reason;
        }

        public static SessionVerdict Judge(string folder, bool gameRunning, DateTime? gameStartedUtc)
        {
            SessionVerdict verdict = new SessionVerdict();
            string path = Path.Combine(folder, FileName);
            bool exists;
            DateTime written = DateTime.MinValue;
            try
            {
                exists = File.Exists(path);
                if (exists) written = File.GetLastWriteTimeUtc(path);
            }
            catch (IOException) { exists = false; }
            catch (UnauthorizedAccessException) { exists = false; }

            if (!gameRunning)
            {
                verdict.Reason = exists
                    ? "The game is not running. Crane's last report was written at " +
                      Clock(written) + ", by an earlier session."
                    : "The game is not running, and Crane has never written a status " +
                      "report in this folder.";
                return verdict;
            }

            if (!exists)
            {
                verdict.Reason = "The game is running, but Crane has not written a status " +
                                 "report in this folder yet. It writes one when it loads the " +
                                 "manifest, a second or two into the session.";
                return verdict;
            }

            if (!gameStartedUtc.HasValue)
            {
                /*
                 * The fallback, and it says so. If this indicator ever misbehaves
                 * again, the first thing worth knowing is whether it was using
                 * the good test or this one -- and that is invisible from the
                 * outside, because both render the same two words.
                 */
                TimeSpan age = DateTime.UtcNow - written;
                verdict.Current = age < Freshness;
                verdict.Reason = "The game is running, but its start time could not be read -- " +
                                 "it may be running elevated while this tool is not. Falling back " +
                                 "to the report's age, which is unreliable: Crane writes this file " +
                                 "when it loads scripts and not again, so a healthy session's " +
                                 "report ages past this window. Last written " + Clock(written) +
                                 ", " + Roughly(age) + " ago; believed for " +
                                 (int)Freshness.TotalMinutes + " minutes.";
                return verdict;
            }

            DateTime started = gameStartedUtc.Value;
            verdict.Current = written >= started;
            verdict.Reason = verdict.Current
                ? "The game started at " + Clock(started) + " and Crane reported at " +
                  Clock(written) + ", after it -- so this report is from the session now " +
                  "running. Its age is not evidence of anything: Crane writes this file when " +
                  "it loads scripts and not again."
                : "The game started at " + Clock(started) + ", but Crane's last report is from " +
                  Clock(written) + " -- before it, so that report describes an earlier session. " +
                  "Waiting for Crane to report on this one.";
            return verdict;
        }

        // Local time, because the user is comparing this against a clock on
        // their own wall and a log printed in local time.
        private static string Clock(DateTime utc)
        {
            return utc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        }

        private static string Roughly(TimeSpan age)
        {
            if (age < TimeSpan.Zero) return "0s";
            if (age.TotalMinutes < 1)
                return (int)age.TotalSeconds + "s";
            if (age.TotalHours < 1)
                return (int)age.TotalMinutes + "m";
            return (int)age.TotalHours + "h " + age.Minutes + "m";
        }

        // The age-only test, kept for the one caller that has no process to
        // compare against: a folder inspected with the game not running.
        public static bool IsFresh(string folder)
        {
            return DescribesCurrentSession(folder, null);
        }

        // Returns null when there is no current readable report -- Crane has not
        // run yet, the file was caught mid-write, or it is left over from an
        // earlier session. All three mean "no information", which the UI shows as
        // Unknown rather than as an error.
        public static StatusReport Read(string folder)
        {
            return Read(folder, null);
        }

        public static StatusReport Read(string folder, DateTime? gameStartedUtc)
        {
            string path = Path.Combine(folder, FileName);
            string text;
            try
            {
                if (!File.Exists(path)) return null;
                if (!DescribesCurrentSession(folder, gameStartedUtc)) return null;
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                                          FileShare.ReadWrite))
                using (StreamReader reader = new StreamReader(stream))
                    text = reader.ReadToEnd();
            }
            catch (IOException) { return null; }

            StatusReport report = new StatusReport();
            report.WritesEnabled = Contains(text, "\"writes\": true") ||
                                   Contains(text, "\"writes\":true");

            // Scanned rather than parsed. The shape is fixed and written by us, so
            // a full parser would be ceremony; and being unable to read one entry
            // must not cost the others.
            int index = 0;
            while (true)
            {
                string file = Field(text, "\"file\"", ref index);
                if (file == null) break;
                int after = index;
                string state = Field(text, "\"state\"", ref after);
                string error = Field(text, "\"error\"", ref after);
                if (state == null) break;
                report.States[file] = Parse(state);
                if (!string.IsNullOrEmpty(error)) report.Errors[file] = error;
                index = after;
            }
            return report;
        }

        private static bool Contains(string text, string needle)
        {
            return text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static ScriptState Parse(string state)
        {
            switch (state.ToLowerInvariant())
            {
                case "loaded": return ScriptState.Loaded;
                case "missing": return ScriptState.Missing;
                case "failed": return ScriptState.Failed;
                case "disabled": return ScriptState.Disabled;
                // Enabled, read, and held back until the game reaches a
                // playable session. Not a failure and not idle: it will run.
                case "waiting": return ScriptState.Waiting;
                default: return ScriptState.Unknown;
            }
        }

        // Finds `"key": "value"` from `index` onward and unescapes the value.
        private static string Field(string text, string key, ref int index)
        {
            int at = text.IndexOf(key, index, StringComparison.Ordinal);
            if (at < 0) return null;
            int colon = text.IndexOf(':', at + key.Length);
            if (colon < 0) return null;
            int open = text.IndexOf('"', colon + 1);
            if (open < 0) return null;

            System.Text.StringBuilder value = new System.Text.StringBuilder();
            int i = open + 1;
            while (i < text.Length && text[i] != '"')
            {
                char c = text[i++];
                if (c == '\\' && i < text.Length)
                {
                    char escape = text[i++];
                    switch (escape)
                    {
                        case 'n': value.Append('\n'); break;
                        case 'r': value.Append('\r'); break;
                        case 't': value.Append('\t'); break;
                        case 'u':
                            if (i + 4 <= text.Length)
                            {
                                int code;
                                if (int.TryParse(text.Substring(i, 4), NumberStyles.HexNumber,
                                                 CultureInfo.InvariantCulture, out code))
                                    value.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: value.Append(escape); break;
                    }
                }
                else value.Append(c);
            }
            index = i;
            return value.ToString();
        }
    }
}
