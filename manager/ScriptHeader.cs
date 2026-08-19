using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace CraneManager
{
    public enum ParamType { Number, Bool, String, Enum }

    public class ParamDecl
    {
        public string Key { get; set; }
        public ParamType Type { get; set; }
        public string Label { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public bool HasMin { get; set; }
        public bool HasMax { get; set; }
        public object Default { get; set; }
        public List<string> Values { get; set; }

        public string Group { get; set; }
        public string Desc { get; set; }

        public ParamDecl()
        {
            Label = "";
            Group = "";
            Desc = "";
            Values = new List<string>();
        }

        public string Describe()
        {
            switch (Type)
            {
                case ParamType.Bool: return "on/off";
                case ParamType.String: return "text";
                case ParamType.Enum: return string.Join(" / ", Values.ToArray());
                default:
                    if (HasMin && HasMax)
                        return string.Format(CultureInfo.InvariantCulture, "{0} to {1}", Min, Max);
                    return "number";
            }
        }
    }

    public static class ScriptHeader
    {
        private const int LinesScanned = 60;

        public static void Read(string path, out string name, out string description,
                                out List<ParamDecl> parameters)
        {
            name = "";
            description = "";
            parameters = new List<ParamDecl>();
            try
            {
                using (StreamReader reader = new StreamReader(path))
                {
                    for (int i = 0; i < LinesScanned; i++)
                    {
                        string line = reader.ReadLine();
                        if (line == null) break;
                        string value;
                        if (TryTag(line, "@name", out value) && name.Length == 0) name = value;
                        else if (TryTag(line, "@description", out value) && description.Length == 0) description = value;
                        else if (TryTag(line, "@param", out value))
                        {
                            ParamDecl declared = ParseParam(value);
                            if (declared != null && Find(parameters, declared.Key) == null)
                                parameters.Add(declared);
                        }
                    }
                }
            }
            catch (IOException)
            {
            }
            if (name.Length == 0) name = Path.GetFileNameWithoutExtension(path);
        }

        public static ParamDecl ParseDeclaration(string text)
        {
            return ParseParam(text);
        }

        public static ParamDecl Find(List<ParamDecl> declared, string key)
        {
            foreach (ParamDecl d in declared)
                if (string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        private static ParamDecl ParseParam(string text)
        {
            List<Token> tokens = Tokenize(text);
            if (tokens.Count < 2) return null;

            ParamDecl declared = new ParamDecl();
            declared.Key = tokens[0].Text;
            if (declared.Key.Length == 0 || declared.Key.Length > 40) return null;

            switch (tokens[1].Text.ToLowerInvariant())
            {
                case "number": declared.Type = ParamType.Number; break;
                case "bool": case "boolean": declared.Type = ParamType.Bool; break;
                case "string": declared.Type = ParamType.String; break;
                case "enum": declared.Type = ParamType.Enum; break;
                default: return null;
            }

            string rawDefault = null;

            string continuing = null;
            for (int i = 2; i < tokens.Count; i++)
            {
                int eq = tokens[i].Text.IndexOf('=');
                if (eq <= 0)
                {
                    if (continuing == null) continue;
                    if (continuing == "label") declared.Label += " " + tokens[i].Text;
                    else if (continuing == "desc") declared.Desc += " " + tokens[i].Text;
                    else if (continuing == "group") declared.Group += " " + tokens[i].Text;
                    continue;
                }
                string key = tokens[i].Text.Substring(0, eq).Trim().ToLowerInvariant();
                string val = tokens[i].Text.Substring(eq + 1).Trim();

                continuing = tokens[i].Quoted ? null : key;
                double number;
                switch (key)
                {
                    case "default": rawDefault = val; break;
                    case "label": declared.Label = val; break;
                    case "desc": case "description": declared.Desc = val; break;
                    case "group": declared.Group = val; break;
                    case "min":
                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                        { declared.Min = number; declared.HasMin = true; }
                        break;
                    case "max":
                        if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
                        { declared.Max = number; declared.HasMax = true; }
                        break;
                    case "values":
                        foreach (string option in val.Split('|'))
                            if (option.Trim().Length > 0) declared.Values.Add(option.Trim());
                        break;
                }
            }

            if (declared.Type == ParamType.Enum && declared.Values.Count == 0) return null;
            if (declared.Label.Length == 0) declared.Label = declared.Key;
            declared.Default = CoerceDefault(declared, rawDefault);
            return declared;
        }

        private static object CoerceDefault(ParamDecl declared, string raw)
        {
            switch (declared.Type)
            {
                case ParamType.Bool:
                    return raw != null &&
                           (raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1");
                case ParamType.Number:
                    double number;
                    if (raw != null && double.TryParse(raw, NumberStyles.Float,
                                                       CultureInfo.InvariantCulture, out number))
                        return number;
                    if (declared.HasMin) return declared.Min;
                    return 0.0;
                case ParamType.Enum:
                    if (raw != null)
                        foreach (string option in declared.Values)
                            if (string.Equals(option, raw, StringComparison.OrdinalIgnoreCase)) return option;
                    return declared.Values.Count > 0 ? declared.Values[0] : "";
                default:
                    return raw != null ? raw : "";
            }
        }

        private struct Token
        {
            public string Text;
            public bool Quoted;
        }

        private static List<Token> Tokenize(string text)
        {
            List<Token> tokens = new List<Token>();
            StringBuilder current = new StringBuilder();
            bool quoted = false, everQuoted = false;
            foreach (char c in text)
            {
                if (c == '"') { quoted = !quoted; everQuoted = true; continue; }
                if (!quoted && (c == ' ' || c == '\t'))
                {
                    if (current.Length > 0)
                    {
                        Token token;
                        token.Text = current.ToString();
                        token.Quoted = everQuoted;
                        tokens.Add(token);
                        current.Length = 0;
                    }
                    everQuoted = false;
                    continue;
                }
                current.Append(c);
            }
            if (current.Length > 0)
            {
                Token last;
                last.Text = current.ToString();
                last.Quoted = everQuoted;
                tokens.Add(last);
            }
            return tokens;
        }

        private static bool TryTag(string line, string tag, out string value)
        {
            value = "";
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("--", StringComparison.Ordinal)) return false;
            trimmed = trimmed.Substring(2).Trim();
            if (!trimmed.StartsWith(tag, StringComparison.OrdinalIgnoreCase)) return false;
            trimmed = trimmed.Substring(tag.Length);
            if (trimmed.Length > 0 && trimmed[0] != ' ' && trimmed[0] != '\t' && trimmed[0] != ':')
                return false;
            value = trimmed.TrimStart(' ', '\t', ':').Trim();
            return value.Length > 0;
        }
    }
}
