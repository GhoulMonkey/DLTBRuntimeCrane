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

namespace CraneManager
{
    public enum ScriptState { Unknown, Disabled, Loaded, Missing, Failed }

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

        // Whether Crane has reported recently enough to be believed. The header
        // bar's "Game: connected" and the per-script dots are the same claim and
        // must not be able to disagree.
        public static bool IsFresh(string folder)
        {
            string path = Path.Combine(folder, FileName);
            try
            {
                if (!File.Exists(path)) return false;
                return DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < Freshness;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // Returns null when there is no current readable report -- Crane has not
        // run yet, the file was caught mid-write, or it is left over from an
        // earlier session. All three mean "no information", which the UI shows as
        // Unknown rather than as an error.
        public static StatusReport Read(string folder)
        {
            string path = Path.Combine(folder, FileName);
            string text;
            try
            {
                if (!File.Exists(path)) return null;
                if (!IsFresh(folder)) return null;
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
