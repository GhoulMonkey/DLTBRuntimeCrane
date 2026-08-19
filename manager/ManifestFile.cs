// DLTBRuntimeCrane.manifest.json reader and writer.
//
// This mirrors ManifestParse.h rather than using a general JSON library. The
// manager must not accept a manifest that DLTBRuntimeCrane.asi will refuse: if
// the tool writes a file the runtime rejects, the failure surfaces in the game
// log instead of in the editor where it can be fixed.
//
// So the same rules apply on both sides -- known keys only, no duplicates, no
// trailing content, and "file" must be a plain name inside scripts\.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CraneManager
{
    public class ScriptEntry
    {
        public string File { get; set; }
        public bool Enabled { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public bool Missing { get; set; }

        // What the runtime reported on its last reload. Distinct from Enabled --
        // Enabled is what the user asked for, State is what actually happened.
        public ScriptState State { get; set; }
        public string StateError { get; set; }

        // Stored values, keyed by parameter name. Values are double, bool or
        // string -- the three the runtime parser accepts, and nothing else.
        public Dictionary<string, object> Params { get; set; }

        // What the script itself declares, re-read from its header whenever the
        // list refreshes. Not persisted: the script is the authority on which
        // knobs exist, and the manifest only on what they are set to.
        public List<ParamDecl> Declared { get; set; }

        public ScriptEntry()
        {
            Enabled = true;
            Name = "";
            Description = "";
            State = ScriptState.Unknown;
            StateError = "";
            Params = new Dictionary<string, object>();
            Declared = new List<ParamDecl>();
        }

        // A stored value whose declaration has gone -- a knob renamed or removed
        // in the script. Kept rather than dropped, and shown greyed: this is a
        // tuning file for experiments, and silently discarding a number somebody
        // chose is worse than showing one stale row. A typo in a declaration
        // would otherwise destroy the value it was meant to name.
        public bool IsOrphan(string key)
        {
            return ScriptHeader.Find(Declared, key) == null;
        }
    }

    public class ManifestException : Exception
    {
        public ManifestException(string message) : base(message) { }
    }

    public static class ManifestFile
    {
        public const int MaxScripts = 64;
        public const int MaxNameLength = 128;
        // Must match CRANE_MANIFEST_MAX_PARAMS in ManifestParse.h.
        public const int MaxParams = 16;

        // ---------------- reading ----------------

        private class Reader
        {
            public string Text;
            public int Pos;
            public int Line = 1;

            public void SkipWhitespace()
            {
                while (Pos < Text.Length)
                {
                    char c = Text[Pos];
                    if (c == '\n') { Line++; Pos++; }
                    else if (c == ' ' || c == '\t' || c == '\r') Pos++;
                    else break;
                }
            }

            public bool Peek(char c)
            {
                SkipWhitespace();
                return Pos < Text.Length && Text[Pos] == c;
            }

            public bool Take(char c)
            {
                if (!Peek(c)) return false;
                Pos++;
                return true;
            }

            public void Expect(char c)
            {
                if (!Take(c)) Fail("expected '" + c + "'");
            }

            public void Fail(string what)
            {
                throw new ManifestException(string.Format(
                    CultureInfo.InvariantCulture, "line {0}: {1}", Line, what));
            }

            public string String()
            {
                Expect('"');
                StringBuilder builder = new StringBuilder();
                while (Pos < Text.Length && Text[Pos] != '"')
                {
                    char c = Text[Pos++];
                    if (c == '\n') Fail("unterminated string");
                    if (c == '\\')
                    {
                        if (Pos >= Text.Length) Fail("unterminated escape");
                        c = Text[Pos++];
                        if (c == 'n') c = '\n';
                        else if (c == 't') c = '\t';
                        else if (c != '"' && c != '\\' && c != '/')
                            Fail("unsupported escape; use \\\" \\\\ \\/ \\n \\t");
                    }
                    if (builder.Length + 1 >= MaxNameLength) Fail("string is too long");
                    builder.Append(c);
                }
                Expect('"');
                return builder.ToString();
            }

            public bool Bool()
            {
                SkipWhitespace();
                if (Pos + 4 <= Text.Length && Text.Substring(Pos, 4) == "true") { Pos += 4; return true; }
                if (Pos + 5 <= Text.Length && Text.Substring(Pos, 5) == "false") { Pos += 5; return false; }
                Fail("expected true or false");
                return false;
            }

            // Mirrors manifest_params in ManifestParse.h: flat, three value
            // types, bounded. The manager must not accept what the ASI refuses.
            public void Params(Dictionary<string, object> into)
            {
                Expect('{');
                if (Peek('}')) { Expect('}'); return; }
                do
                {
                    if (Peek('}')) break;
                    string key = String();
                    if (key.Length == 0) Fail("a parameter has an empty name");
                    Expect(':');
                    SkipWhitespace();
                    if (Pos >= Text.Length) Fail("expected a parameter value");
                    char c = Text[Pos];
                    object value;
                    if (c == '"') value = String();
                    else if (c == 't' || c == 'f') value = Bool();
                    else if (c == '{' || c == '[') { Fail("parameter values cannot be objects or arrays"); return; }
                    else value = Number();
                    foreach (string seen in into.Keys)
                        if (string.Equals(seen, key, StringComparison.OrdinalIgnoreCase))
                        { Fail("duplicate parameter name"); return; }
                    if (into.Count >= MaxParams) { Fail("too many parameters for one script"); return; }
                    into[key] = value;
                } while (Take(','));
                Expect('}');
            }

            public double Number()
            {
                SkipWhitespace();
                int start = Pos;
                if (Pos < Text.Length && (Text[Pos] == '-' || Text[Pos] == '+')) Pos++;
                int digits = 0;
                while (Pos < Text.Length &&
                       ((Text[Pos] >= '0' && Text[Pos] <= '9') || Text[Pos] == '.' ||
                        Text[Pos] == 'e' || Text[Pos] == 'E' || Text[Pos] == '-' || Text[Pos] == '+'))
                {
                    if (Text[Pos] >= '0' && Text[Pos] <= '9') digits++;
                    Pos++;
                }
                if (digits == 0) Fail("expected a number");
                double parsed;
                if (!double.TryParse(Text.Substring(start, Pos - start), NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out parsed))
                    Fail("could not read that number");
                return parsed;
            }

            public void SkipNumber()
            {
                SkipWhitespace();
                int digits = 0;
                if (Pos < Text.Length && (Text[Pos] == '-' || Text[Pos] == '+')) Pos++;
                while (Pos < Text.Length && Text[Pos] >= '0' && Text[Pos] <= '9') { Pos++; digits++; }
                if (digits == 0) Fail("expected a number");
            }
        }

        // Same containment rule as manifest_valid_name in ManifestParse.h.
        public static bool IsValidScriptName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (name.Length >= MaxNameLength) return false;
            if (name.IndexOf("..", StringComparison.Ordinal) >= 0) return false;
            if (name.Length > 1 && name[1] == ':') return false;
            if (name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0) return false;
            return true;
        }

        public static List<ScriptEntry> Read(string text)
        {
            List<ScriptEntry> entries = new List<ScriptEntry>();
            Reader reader = new Reader();
            reader.Text = text;

            reader.Expect('{');
            do
            {
                if (reader.Peek('}')) break;
                string key = reader.String();
                reader.Expect(':');
                if (key == "version")
                {
                    reader.SkipNumber();
                }
                else if (key == "scripts")
                {
                    reader.Expect('[');
                    if (!reader.Peek(']'))
                    {
                        do
                        {
                            ScriptEntry entry = new ScriptEntry();
                            bool haveFile = false;
                            reader.Expect('{');
                            do
                            {
                                if (reader.Peek('}')) break;
                                string field = reader.String();
                                reader.Expect(':');
                                if (field == "file") { entry.File = reader.String(); haveFile = true; }
                                else if (field == "enabled") entry.Enabled = reader.Bool();
                                else if (field == "params") reader.Params(entry.Params);
                                else reader.Fail("unknown key in a scripts entry; expected file, enabled or params");
                            } while (reader.Take(','));
                            reader.Expect('}');
                            if (!haveFile) reader.Fail("a scripts entry has no \"file\"");
                            if (!IsValidScriptName(entry.File))
                                reader.Fail("\"file\" must be a plain name inside scripts\\");
                            if (entries.Count == MaxScripts) reader.Fail("too many scripts");
                            foreach (ScriptEntry existing in entries)
                                if (string.Equals(existing.File, entry.File, StringComparison.OrdinalIgnoreCase))
                                    reader.Fail("duplicate \"file\" entry");
                            entries.Add(entry);
                        } while (reader.Take(','));
                    }
                    reader.Expect(']');
                }
                else
                {
                    reader.Fail("unknown top-level key; expected version or scripts");
                }
            } while (reader.Take(','));
            reader.Expect('}');

            reader.SkipWhitespace();
            if (reader.Pos != reader.Text.Length)
                reader.Fail("unexpected content after the closing '}'");

            return entries;
        }

        // ---------------- writing ----------------

        // Numbers are written round-trippably ("R"), because a value somebody
        // tuned must come back as the number they set, not a rounding of it.
        private static string Literal(object value)
        {
            if (value is bool) return ((bool)value) ? "true" : "false";
            if (value is double) return ((double)value).ToString("R", CultureInfo.InvariantCulture);
            return "\"" + Escape(Convert.ToString(value, CultureInfo.InvariantCulture)) + "\"";
        }

        private static string Escape(string value)
        {
            StringBuilder builder = new StringBuilder();
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') builder.Append('\\').Append(c);
                else if (c == '\n') builder.Append("\\n");
                else if (c == '\t') builder.Append("\\t");
                else builder.Append(c);
            }
            return builder.ToString();
        }

        // Written to be readable and diffable: the manifest stays hand-editable,
        // which is what keeps this tool optional rather than required.
        public static string Write(IList<ScriptEntry> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("{\r\n  \"version\": 1,\r\n  \"scripts\": [");
            for (int i = 0; i < entries.Count; i++)
            {
                builder.Append(i == 0 ? "\r\n" : ",\r\n");
                builder.Append(string.Format(CultureInfo.InvariantCulture,
                    "    {{ \"file\": \"{0}\", \"enabled\": {1}",
                    Escape(entries[i].File), entries[i].Enabled ? "true" : "false"));
                if (entries[i].Params.Count > 0)
                {
                    builder.Append(", \"params\": {");
                    bool firstParam = true;
                    foreach (KeyValuePair<string, object> pair in entries[i].Params)
                    {
                        builder.Append(firstParam ? " " : ", ");
                        firstParam = false;
                        builder.Append(string.Format(CultureInfo.InvariantCulture,
                            "\"{0}\": {1}", Escape(pair.Key), Literal(pair.Value)));
                    }
                    builder.Append(" }");
                }
                builder.Append(" }");
            }
            if (entries.Count > 0) builder.Append("\r\n  ");
            builder.Append("]\r\n}\r\n");
            return builder.ToString();
        }

        public static void Save(string path, IList<ScriptEntry> entries)
        {
            // UTF-8 without a BOM: the runtime reads raw bytes and a BOM would
            // land in front of the opening brace.
            File.WriteAllText(path, Write(entries), new UTF8Encoding(false));
        }
    }
}
