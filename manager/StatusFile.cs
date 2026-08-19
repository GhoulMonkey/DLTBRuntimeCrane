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

        public static readonly TimeSpan Freshness = TimeSpan.FromMinutes(5);

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
