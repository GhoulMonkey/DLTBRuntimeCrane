// Reads and edits an annotated client INI.
//
// The other half of the manager's job. Crane's own scripts declare their
// settings in Lua header comments; the native Bridge clients declare theirs in
// an INI, and until now the only way to change one was to open it in a text
// editor and hope you remembered which values were legal.
//
// Three rules shaped this:
//
// 1. An annotated INI is still an INI. Every annotation is a comment. A client
//    that knows nothing about the manager reads its INI exactly as before, and
//    an INI annotated for a newer manager still loads in an older one. Hence no
//    sidecar .json: a second file describing the first is a second file to get
//    out of sync, and users edit INIs by hand.
//
// 2. The declarations are UI assistance rather than a contract. The client
//    parses its own INI and enforces its own bounds, exactly as it does today.
//    Nothing here reaches the runtime. Same distinction the Lua side turns on,
//    and it means a hand-edited INI and a manager-edited one are
//    indistinguishable to the mod.
//
// 3. Editing preserves the file. These INIs carry substantial prose, and that
//    prose is the documentation the user has. Writing a value rewrites one line
//    and leaves comments, blank lines, section order and unknown keys exactly as
//    they were.
//
// Sections are part of a key's identity, and this is the correction that Beastly
// Hunger forced. The first version keyed settings by bare name and treated a
// repeated name as ambiguous, refusing to write it -- reasoning that a duplicate
// was probably a leftover. Beastly Hunger then turned up with `Speed.Full` under
// both [BeastlyHunger.MovementSpeed] and [BeastlyHunger.MeleeAttackSpeed], plus
// `Damage.*` and `Penalty.*` in two sections each: fifteen keys unmanageable, on
// one of the flagship mods. Repeating a name across sections is not a mistake in
// an INI, it is the point of having sections.
//
// So a declaration is scoped to the section it appears in:
//
//   [BeastlyHunger.MovementSpeed]
//   ; @param Speed.Full number default=110 min=50 max=200 label="Full"
//   Speed.Full=110
//
// and may name a section explicitly when the annotations are gathered in one
// block instead:
//
//   ; @param [BeastlyHunger.MovementSpeed]Speed.Full number default=110
//
// A bracketed prefix rather than a dotted one, because these INIs already use
// dots inside key names and `Speed.Full` would be unsplittable.
//
// The annotations, all comments:
//
//   ; @name Time Lapse
//   ; @description Hold a key to make the game run faster.
//   ; @enable Enabled
//   ; @param Factor number default=10 min=1 max=1000 label="Speed" group="Speed"
//
// @param names its key explicitly rather than describing "the next key below",
// so moving a key or its comment block cannot silently re-point a declaration at
// the wrong setting.
//
// @enable is what earns a checkbox. A client with no on/off key does not get one
// -- a checkbox that cannot be unticked overstates what the tool can do.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CraneManager
{
    // One declared setting, and the section its key lives in.
    public class IniSetting
    {
        public ParamDecl Decl { get; set; }
        public string Section { get; set; }

        public IniSetting(ParamDecl decl, string section)
        {
            Decl = decl;
            Section = section == null ? "" : section;
        }

        public string Key { get { return Decl.Key; } }
    }

    public class IniClient
    {
        public string Path { get; set; }
        public string File { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string EnableKey { get; set; }
        public string EnableSection { get; set; }
        public List<IniSetting> Declared { get; set; }
        // Raw text as it appears in the file, keyed by section and key.
        public Dictionary<string, string> Values { get; set; }

        public IniClient()
        {
            Path = "";
            File = "";
            Name = "";
            Description = "";
            EnableKey = "";
            EnableSection = "";
            Declared = new List<IniSetting>();
            Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        // Section and key are joined by a character that cannot occur in either,
        // so no pair of real names can collide into one entry.
        public static string Slot(string section, string key)
        {
            return (section == null ? "" : section) + "\u001F" + key;
        }

        public bool Has(string section, string key)
        {
            return Values.ContainsKey(Slot(section, key));
        }

        public string Raw(string section, string key)
        {
            string value;
            return Values.TryGetValue(Slot(section, key), out value) ? value : "";
        }

        public bool HasEnable
        {
            get { return EnableKey.Length > 0 && Has(EnableSection, EnableKey); }
        }

        public bool Enabled
        {
            get { return HasEnable && IniFile.IsTruthy(Raw(EnableSection, EnableKey)); }
        }
    }

    public static class IniFile
    {
        // INIs spell booleans 1/0, not true/false. Accepting the words too costs
        // nothing and saves a user who typed the obvious thing.
        public static bool IsTruthy(string raw)
        {
            if (raw == null) return false;
            string trimmed = raw.Trim();
            return trimmed == "1" ||
                   trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        // Written back in the spelling these files already use.
        public static string FromBool(bool value) { return value ? "1" : "0"; }

        private static bool IsComment(string trimmed)
        {
            return trimmed.Length > 0 && (trimmed[0] == ';' || trimmed[0] == '#');
        }

        private static string SectionOf(string trimmed)
        {
            if (trimmed.Length < 2 || trimmed[0] != '[') return null;
            int close = trimmed.IndexOf(']');
            if (close <= 1) return null;
            return trimmed.Substring(1, close - 1).Trim();
        }

        // "[Section]Key rest..." -> section "Section", remainder "Key rest...".
        private static string SplitSection(string text, out string remainder)
        {
            remainder = text;
            if (text.Length == 0 || text[0] != '[') return null;
            int close = text.IndexOf(']');
            if (close <= 1) return null;
            remainder = text.Substring(close + 1).TrimStart();
            return text.Substring(1, close - 1).Trim();
        }

        /*
         * Reads an INI, returning null when it declares nothing.
         *
         * Null rather than an empty client: an unannotated INI is not something
         * this tool can manage, and listing it with no settings would invite the
         * user to click something that does nothing. Annotating it is how a mod
         * opts in.
         */
        public static IniClient Read(string path)
        {
            return Read(path, true);
        }

        /*
         * requireDeclarations=false returns the file's values even when it
         * declares nothing.
         *
         * Needed because CRANE's own INI is generated on first run and then never
         * rewritten, so every install made before the annotations existed still
         * has an unannotated file. Annotating the template only ever helps fresh
         * installs; the setting has to be reachable regardless of when the file
         * was created, which means the manager supplies the declarations for its
         * own INI rather than reading them back out of it.
         */
        public static IniClient Read(string path, bool requireDeclarations)
        {
            string[] lines;
            try { lines = System.IO.File.ReadAllLines(path); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }

            IniClient client = new IniClient();
            client.Path = path;
            client.File = System.IO.Path.GetFileName(path);

            string section = "";
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0) continue;

                if (IsComment(trimmed))
                {
                    string body = trimmed.Substring(1).Trim();
                    string value;
                    if (Tag(body, "@name", out value) && client.Name.Length == 0)
                        client.Name = value;
                    else if (Tag(body, "@description", out value) && client.Description.Length == 0)
                        client.Description = value;
                    else if (Tag(body, "@enable", out value) && client.EnableKey.Length == 0)
                    {
                        string rest;
                        string explicitSection = SplitSection(value, out rest);
                        client.EnableSection = explicitSection != null ? explicitSection : section;
                        client.EnableKey = rest.Trim();
                    }
                    else if (Tag(body, "@param", out value))
                    {
                        string rest;
                        string explicitSection = SplitSection(value, out rest);
                        ParamDecl declared = ScriptHeader.ParseDeclaration(rest);
                        if (declared != null)
                        {
                            string owner = explicitSection != null ? explicitSection : section;
                            if (Find(client, owner, declared.Key) == null)
                                client.Declared.Add(new IniSetting(declared, owner));
                        }
                    }
                    continue;
                }

                string header = SectionOf(trimmed);
                if (header != null)
                {
                    /*
                     * Declarations written above the first section header adopt
                     * it.
                     *
                     * Putting the whole annotation block at the top of the file,
                     * before any [Section], is the natural way to write it and is
                     * what a banner comment looks like. Leaving those unscoped
                     * would silently bind them to no section and render every
                     * setting as "declared but absent", which is a confusing
                     * failure for a file that looks obviously correct.
                     *
                     * Only the first header adopts them. A declaration sitting
                     * inside a later section already carries that section, and
                     * retro-binding across several sections would be guessing.
                     */
                    if (section.Length == 0)
                    {
                        foreach (IniSetting pending in client.Declared)
                            if (pending.Section.Length == 0) pending.Section = header;
                        if (client.EnableKey.Length > 0 && client.EnableSection.Length == 0)
                            client.EnableSection = header;
                    }
                    section = header;
                    continue;
                }

                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;
                string key = trimmed.Substring(0, eq).Trim();
                if (key.Length == 0) continue;
                string slot = IniClient.Slot(section, key);
                // A key repeated inside a single section is genuinely
                // ambiguous; the first wins, which is what an INI parser
                // conventionally does.
                if (!client.Values.ContainsKey(slot))
                    client.Values[slot] = trimmed.Substring(eq + 1).Trim();
            }

            if (requireDeclarations &&
                client.Declared.Count == 0 && client.EnableKey.Length == 0) return null;
            if (client.Name.Length == 0)
                client.Name = System.IO.Path.GetFileNameWithoutExtension(path);
            return client;
        }

        public static IniSetting Find(IniClient client, string section, string key)
        {
            foreach (IniSetting setting in client.Declared)
                if (string.Equals(setting.Section, section, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(setting.Key, key, StringComparison.OrdinalIgnoreCase))
                    return setting;
            return null;
        }

        /*
         * Rewrites one key's value inside one section, leaving every other byte
         * alone.
         *
         * Line-oriented rather than parse-and-serialise, because a serialiser
         * would have to reproduce comments, blank lines, spacing and section
         * order perfectly to avoid mangling files whose prose is their
         * documentation, and it would only have to get it wrong once.
         *
         * Returns false without touching the file when the key is not present in
         * that section, so a caller cannot append a stray key to somebody's
         * config or write the same name in the wrong section.
         */
        public static bool Write(string path, string section, string key, string value)
        {
            string[] lines;
            try { lines = System.IO.File.ReadAllLines(path); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }

            string current = "";
            int found = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0 || IsComment(trimmed)) continue;
                string header = SectionOf(trimmed);
                if (header != null) { current = header; continue; }
                int eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;
                if (!string.Equals(current, section, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(trimmed.Substring(0, eq).Trim(), key,
                                   StringComparison.OrdinalIgnoreCase)) continue;
                found = i;
                break;      // first match in the named section, matching Read
            }
            if (found < 0) return false;

            // Keep whatever indentation and spacing the line already had.
            string original = lines[found];
            int at = original.IndexOf('=');
            lines[found] = original.Substring(0, at + 1) + value;

            try
            {
                // No BOM: these files are read by C clients doing byte-oriented
                // parsing, and a BOM on the first line has broken a key before.
                System.IO.File.WriteAllLines(path, lines, new UTF8Encoding(false));
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            return true;
        }

        // The declared type decides how a raw INI string becomes an editor value.
        public static object ToValue(ParamDecl declared, string raw)
        {
            switch (declared.Type)
            {
                case ParamType.Bool:
                    return IsTruthy(raw);
                case ParamType.Number:
                    double number;
                    if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture,
                                        out number)) return number;
                    return declared.Default is double ? declared.Default : (object)0.0;
                default:
                    return raw;
            }
        }

        public static string ToRaw(ParamDecl declared, object value)
        {
            if (declared.Type == ParamType.Bool)
                return FromBool(value is bool && (bool)value);
            if (value is double)
            {
                double number = (double)value;
                // Whole numbers written without a decimal point: these are key
                // codes, percentages and millisecond counts, and "119" is what
                // the file and its own comments already say.
                if (number == Math.Floor(number) && Math.Abs(number) < 1e15)
                    return ((long)number).ToString(CultureInfo.InvariantCulture);
                return number.ToString("R", CultureInfo.InvariantCulture);
            }
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static bool Tag(string body, string tag, out string value)
        {
            value = "";
            if (!body.StartsWith(tag, StringComparison.OrdinalIgnoreCase)) return false;
            string rest = body.Substring(tag.Length);
            if (rest.Length > 0 && rest[0] != ' ' && rest[0] != '\t' && rest[0] != ':')
                return false;   // "@nameless" must not match "@name"
            value = rest.TrimStart(' ', '\t', ':').Trim();
            return value.Length > 0;
        }
    }
}
