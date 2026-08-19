using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CraneManager
{
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
        public static bool IsTruthy(string raw)
        {
            if (raw == null) return false;
            string trimmed = raw.Trim();
            return trimmed == "1" ||
                   trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

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

        private static string SplitSection(string text, out string remainder)
        {
            remainder = text;
            if (text.Length == 0 || text[0] != '[') return null;
            int close = text.IndexOf(']');
            if (close <= 1) return null;
            remainder = text.Substring(close + 1).TrimStart();
            return text.Substring(1, close - 1).Trim();
        }

        public static IniClient Read(string path)
        {
            return Read(path, true);
        }

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
                break;
            }
            if (found < 0) return false;

            string original = lines[found];
            int at = original.IndexOf('=');
            lines[found] = original.Substring(0, at + 1) + value;

            try
            {
                System.IO.File.WriteAllLines(path, lines, new UTF8Encoding(false));
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            return true;
        }

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
                return false;
            value = rest.TrimStart(' ', '\t', ':').Trim();
            return value.Length > 0;
        }
    }
}
