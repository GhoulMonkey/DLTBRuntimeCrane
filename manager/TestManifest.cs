using System;
using System.Collections.Generic;
using System.IO;
using CraneManager;

internal static class TestManifest
{
    private static int _failures;
    private static int _checks;

    private static void Check(bool condition, string what)
    {
        _checks++;
        if (!condition) { _failures++; Console.WriteLine("  FAIL: " + what); }
    }

    private static bool Parses(string text, out int count, out string error)
    {
        count = 0;
        error = "";
        try
        {
            List<ScriptEntry> entries = ManifestFile.Read(text);
            count = entries.Count;
            return true;
        }
        catch (ManifestException e)
        {
            error = e.Message;
            return false;
        }
    }

    private static void Accepts(string label, string text, int expected)
    {
        int count;
        string error;
        bool ok = Parses(text, out count, out error);
        Console.WriteLine("accept " + label);
        Check(ok, "should parse");
        if (!ok) { Console.WriteLine("    error was: " + error); return; }
        Check(count == expected, "entry count");
    }

    private static void Rejects(string label, string text)
    {
        int count;
        string error;
        bool ok = Parses(text, out count, out error);
        Console.WriteLine("reject " + label);
        Check(!ok, "should be refused");
        if (!ok)
        {
            Check(error.Length > 0, "carries a reason");
            Console.WriteLine("    -> " + error);
        }
    }

    private static readonly string[] AgreementCases = new string[]
    {
        "{\"scripts\":[{\"file\":\"a.lua\"",
        "{\"scripts\":[{\"file\":\"a.lua\"},]}",
        "{\"scrpits\":[]}",
        "{\"scripts\":[{\"file\":\"a.lua\",\"order\":2}]}",
        "{\"scripts\":[{\"enabled\":true}]}",
        "{\"scripts\":[{\"file\":\"a.lua\",\"enabled\":1}]}",
        "{\"scripts\":[{\"file\":\"a.lua\"},{\"file\":\"A.LUA\"}]}",
        "{\"scripts\":[]} {\"scripts\":[]}",
        "{\"scripts\":[{\"file\":\"a.lua}]}",
        "{\"version\":\"one\",\"scripts\":[]}",
        "{\"scripts\":[{\"file\":\"..\\\\evil.lua\"}]}",
        "{\"scripts\":[{\"file\":\"sub/evil.lua\"}]}",
        "{\"scripts\":[{\"file\":\"sub\\\\evil.lua\"}]}",
        "{\"scripts\":[{\"file\":\"C:\\\\evil.lua\"}]}",
        "{\"scripts\":[{\"file\":\"\"}]}"
    };

    private static int Main()
    {
        Accepts("minimal", "{\"scripts\":[]}", 0);
        Accepts("one script", "{\"version\":1,\"scripts\":[{\"file\":\"a.lua\",\"enabled\":true}]}", 1);
        Accepts("enabled defaults to true", "{\"scripts\":[{\"file\":\"a.lua\"}]}", 1);
        Accepts("ordered pair", "{\"scripts\":[{\"file\":\"a.lua\"},{\"file\":\"b.lua\",\"enabled\":false}]}", 2);
        Accepts("whitespace and newlines",
                "{\n  \"version\" : 1 ,\n  \"scripts\" : [\n    { \"file\" : \"a.lua\" }\n  ]\n}\n", 1);
        Accepts("empty object", "{}", 0);

        Console.WriteLine();
        Console.WriteLine("agreement with tools\\test_manifest.c");
        Check(AgreementCases.Length == 15, "case list still has 15 entries; update both lists together");
        foreach (string text in AgreementCases)
        {
            int count;
            string error;
            Check(!Parses(text, out count, out error), "runtime rejects this, so the manager must too");
        }

        Console.WriteLine();
        Console.WriteLine("round trip");
        List<ScriptEntry> written = new List<ScriptEntry>();
        written.Add(NewEntry("startup.lua", true));
        written.Add(NewEntry("godmode.lua", false));
        written.Add(NewEntry("probe.lua", true));
        string text2 = ManifestFile.Write(written);
        int backCount;
        string backError;
        Check(Parses(text2, out backCount, out backError), "written manifest parses: " + backError);
        Check(backCount == 3, "round trip preserves the entry count");
        List<ScriptEntry> back = ManifestFile.Read(text2);
        for (int i = 0; i < written.Count; i++)
        {
            Check(back[i].File == written[i].File, "round trip preserves order and name at " + i);
            Check(back[i].Enabled == written[i].Enabled, "round trip preserves enabled at " + i);
        }

        Check(ManifestFile.Read(ManifestFile.Write(new List<ScriptEntry>())).Count == 0,
              "an empty list round trips");

        Console.WriteLine();
        Console.WriteLine("parameters");
        Accepts("empty params", "{\"scripts\":[{\"file\":\"a.lua\",\"params\":{}}]}", 1);
        Accepts("three value types",
                "{\"scripts\":[{\"file\":\"a.lua\",\"params\":{\"speed\":1.5,\"loud\":false,\"mode\":\"fast\"}}]}", 1);
        Rejects("param object value", "{\"scripts\":[{\"file\":\"a.lua\",\"params\":{\"a\":{}}}]}");
        Rejects("param array value", "{\"scripts\":[{\"file\":\"a.lua\",\"params\":{\"a\":[1]}}]}");
        Rejects("duplicate param", "{\"scripts\":[{\"file\":\"a.lua\",\"params\":{\"a\":1,\"A\":2}}]}");

        Console.WriteLine("parameter values survive a round trip");
        {
            List<ScriptEntry> withParams = new List<ScriptEntry>();
            ScriptEntry e = NewEntry("godmode.lua", true);
            e.Params["incoming"] = -0.9;
            e.Params["outgoing"] = 9.0;
            e.Params["announce"] = true;
            e.Params["mode"] = "fast";
            withParams.Add(e);

            string text = ManifestFile.Write(withParams);
            int count; string error;
            Check(Parses(text, out count, out error), "written params parse: " + error);

            List<ScriptEntry> readBack = ManifestFile.Read(text);
            Check(readBack.Count == 1, "one entry back");
            Check(readBack[0].Params.Count == 4, "four parameters back");

            Check((double)readBack[0].Params["incoming"] == -0.9, "double survives exactly");
            Check((double)readBack[0].Params["outgoing"] == 9.0, "whole number survives");
            Check((bool)readBack[0].Params["announce"], "bool survives");
            Check((string)readBack[0].Params["mode"] == "fast", "string survives");
        }

        Console.WriteLine("declaration parsing");
        {
            string path = Path.GetTempFileName();
            File.WriteAllText(path,
                "-- @name Demo\r\n" +
                "-- @description A demo script\r\n" +
                "-- @param incoming number default=-0.9 min=-1.0 max=0.0 label=\"Incoming damage\"\r\n" +
                "-- @param loud bool default=true label=\"Be noisy\"\r\n" +
                "-- @param mode enum values=slow|fast default=fast\r\n" +
                "-- @nameless should not be read as a name\r\n" +
                "bridge.log('hi')\r\n");
            string name, description;
            List<ParamDecl> declared;
            ScriptHeader.Read(path, out name, out description, out declared);
            File.Delete(path);

            Check(name == "Demo", "name parsed");
            Check(description == "A demo script", "description parsed");
            Check(declared.Count == 3, "three parameters declared");

            ParamDecl incoming = ScriptHeader.Find(declared, "incoming");
            Check(incoming != null, "incoming found");
            Check(incoming.Type == ParamType.Number, "incoming is a number");
            Check(incoming.HasMin && incoming.Min == -1.0, "min parsed");
            Check(incoming.HasMax && incoming.Max == 0.0, "max parsed");
            Check((double)incoming.Default == -0.9, "default parsed");
            Check(incoming.Label == "Incoming damage", "quoted label kept its space");

            ParamDecl loud = ScriptHeader.Find(declared, "loud");
            Check(loud != null && loud.Type == ParamType.Bool, "bool declared");
            Check(loud != null && (bool)loud.Default, "bool default parsed");

            ParamDecl mode = ScriptHeader.Find(declared, "mode");
            Check(mode != null && mode.Type == ParamType.Enum, "enum declared");
            Check(mode != null && mode.Values.Count == 2, "enum values parsed");
            Check(mode != null && (string)mode.Default == "fast", "enum default parsed");

            Check(mode != null && mode.Label == "mode", "missing label falls back to the key");
        }

        Console.WriteLine("keep-and-flag");
        {
            ScriptEntry e = NewEntry("a.lua", true);
            e.Params["kept"] = 1.0;
            e.Params["orphan"] = 2.0;
            ParamDecl only = new ParamDecl();
            only.Key = "kept";
            only.Type = ParamType.Number;
            e.Declared.Add(only);
            Check(!e.IsOrphan("kept"), "declared value is not an orphan");
            Check(e.IsOrphan("orphan"), "undeclared value is flagged");
            Check(e.Params.ContainsKey("orphan"), "and is kept, not dropped");
        }

        Console.WriteLine();
        Console.WriteLine("generated defaults");
        {
            string ini = FirstRun.IniTemplate;
            Check(ini.Contains("AllowWrites=0"), "generated INI ships writes OFF");
            Check(!ini.Contains("AllowWrites=1"), "and never ships them on");
            Check(ini.Contains("[Crane]"), "has the section Crane reads");

            Check(ini.Contains("unsandboxed"), "keeps the warning about unsandboxed code");
            Check(ini.Contains("LogLevel"), "documents LogLevel too");

            {
                string dir = Path.Combine(Path.GetTempPath(), "crane-first-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                Directory.CreateDirectory(Path.Combine(dir, "scripts"));
                File.WriteAllText(Path.Combine(dir, "scripts", "quick_hands.lua"), "-- @name Quick Hands\n");
                FirstRun.Create(dir);

                List<ScriptEntry> listed = ManifestFile.Read(
                    File.ReadAllText(Path.Combine(dir, "DLTBRuntimeCrane.manifest.json")));
                Check(listed.Count == 0, "first run lists no scripts, even a deployed one");
                Check(File.Exists(Path.Combine(dir, "DLTBRuntimeCrane.ini")),
                      "but it does create the INI");
                Check(!File.Exists(Path.Combine(dir, "scripts", "startup.lua")),
                      "and generates no example script");
                Directory.Delete(dir, true);
            }
        }

        Console.WriteLine();
        Console.WriteLine("status file");
        {
            string dir = Path.Combine(Path.GetTempPath(), "crane_status_test");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, StatusFile.FileName);

            File.WriteAllText(path,
                "{\n  \"version\": 1,\n  \"writes\": true,\n  \"scripts\": [" +
                "\n    { \"file\": \"startup.lua\", \"state\": \"loaded\", \"error\": \"\" }," +
                "\n    { \"file\": \"quick_hands.lua\", \"state\": \"failed\", \"error\":" +
                " \"quick_hands.lua:47: attempt to call a nil value (field \\\"claim\\\")\\nstack traceback:\" }," +
                "\n    { \"file\": \"godmode.lua\", \"state\": \"disabled\", \"error\": \"\" }," +
                "\n    { \"file\": \"gone.lua\", \"state\": \"missing\", \"error\": \"not found in scripts\\\\\" }" +
                "\n  ]\n}\n");

            StatusReport report = StatusFile.Read(dir);
            Check(report != null, "report parses");
            if (report != null)
            {
                Check(report.WritesEnabled, "writes flag read");
                Check(report.StateOf("startup.lua") == ScriptState.Loaded, "loaded state");
                Check(report.StateOf("quick_hands.lua") == ScriptState.Failed, "failed state");
                Check(report.StateOf("godmode.lua") == ScriptState.Disabled, "disabled state");
                Check(report.StateOf("gone.lua") == ScriptState.Missing, "missing state");

                Check(report.StateOf("never_heard_of.lua") == ScriptState.Unknown,
                      "absent script reads as Unknown");

                string error = report.ErrorOf("quick_hands.lua");
                Check(error.Contains("attempt to call a nil value"), "error text survives");
                Check(error.Contains("\"claim\""), "escaped quotes are unescaped");
                Check(error.Contains("\n"), "escaped newline is unescaped");
                Check(report.ErrorOf("gone.lua").EndsWith("\\"), "escaped backslash survives");
                Check(report.ErrorOf("startup.lua") == "", "empty error stays empty");
            }

            File.WriteAllText(path, "{\n  \"version\": 1,\n  \"scripts\": [\n    { \"file\": \"a.lua\"");
            StatusReport partial = StatusFile.Read(dir);
            Check(partial != null, "a truncated report still returns something");

            File.Delete(path);
            Check(StatusFile.Read(dir) == null, "no file reads as no report");
            Directory.Delete(dir, true);
        }

        {
            Console.WriteLine();
            Console.WriteLine("== annotated client INIs ==");

            string dir = Path.Combine(Path.GetTempPath(), "crane-ini-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "TestClient.ini");

            string[] original = new string[]
            {
                "; Test Client 1.0",
                "; @name Test Client",
                "; @description Does a thing.",
                "; @enable Enabled",
                "; @param Factor number default=10 min=1 max=1000 label=\"Speed\" group=\"Speed\" desc=\"How fast.\"",
                "; @param Mode enum values=Off|Info|Debug default=Info label=\"Mode\"",
                "; @param Missing bool default=true label=\"Absent key\"",
                "",
                "[Test]",
                "",
                "; Prose that must survive.",
                "Enabled=1",
                "Factor  =  10",
                "Mode=Info",
                "Unknown=leave me alone",
                ""
            };
            File.WriteAllLines(path, original);

            IniClient client = IniFile.Read(path);
            Check(client != null, "an annotated INI is read");
            Check(client.Name == "Test Client", "@name is read");
            Check(client.EnableKey == "Enabled" && client.HasEnable, "@enable names the on/off key");
            Check(client.Enabled, "and 1 reads as on");
            Check(client.Declared.Count == 3, "three parameters are declared");
            Check(client.Raw("Test", "Factor") == "10", "values survive surrounding whitespace");
            Check(client.Raw("Test", "Unknown") == "leave me alone", "undeclared keys are still read");

            IniSetting factor = IniFile.Find(client, "Test", "Factor");
            Check(factor != null, "a declaration above the section header binds to it");
            Check(factor != null && factor.Decl.Group == "Speed",
                  "the Lua grammar's group= works here too");
            Check(factor != null && factor.Decl.Desc == "How fast.", "and desc=");

            Check(!client.Has("Test", "Missing"),
                  "a declared-but-absent key does not appear as a value");
            Check(!IniFile.Write(path, "Test", "Missing", "0"),
                  "and writing it is refused rather than appending a stray key");

            Check(IniFile.Write(path, "Test", "Factor", "250"), "a declared key can be written");
            string[] after = File.ReadAllLines(path);
            Check(after.Length == original.Length, "the line count is unchanged");
            Check(after[10] == "; Prose that must survive.", "comments are untouched");
            Check(after[8] == "[Test]", "the section header is untouched");
            Check(after[14] == "Unknown=leave me alone", "unrelated keys are untouched");
            Check(after[12].StartsWith("Factor  ="), "the key's own spacing is preserved");
            Check(IniFile.Read(path).Raw("Test", "Factor") == "250", "and the new value reads back");

            Check(IniFile.FromBool(false) == "0", "false writes as 0");
            Check(IniFile.IsTruthy("1") && !IniFile.IsTruthy("0"), "and 1/0 read back");
            Check(IniFile.Write(path, "Test", "Enabled", "0"), "the enable key can be written");
            Check(!IniFile.Read(path).Enabled, "which turns the client off");

            Check(IniFile.ToRaw(factor.Decl, 119.0) == "119",
                  "a whole number writes without a decimal point");

            string plain = Path.Combine(dir, "Plain.ini");
            File.WriteAllLines(plain, new string[] { "[X]", "Key=1" });
            Check(IniFile.Read(plain) == null, "an unannotated INI is not listed as a client");

            Directory.Delete(dir, true);
        }

        {
            Console.WriteLine();
            Console.WriteLine("== the generated CRANE INI ==");

            string dir = Path.Combine(Path.GetTempPath(), "crane-self-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "DLTBRuntimeCrane.ini");
            File.WriteAllText(path, FirstRun.IniTemplate);

            IniClient self = IniFile.Read(path);
            Check(self != null, "the generated INI parses as an annotated client");
            Check(self != null && self.Name == "CRANE", "and names itself CRANE");

            IniSetting writes = IniFile.Find(self, "Crane", "AllowWrites");
            Check(writes != null, "AllowWrites is declared, so Settings can offer it");
            Check(writes != null && writes.Decl.Type == ParamType.Bool, "as a checkbox");
            Check(writes != null && writes.Decl.Group == "Safety", "under Safety");

            Check(self != null && self.Raw("Crane", "AllowWrites") == "0",
                  "and ships OFF");

            Check(self != null && !self.HasEnable, "the host declares no enable key");

            Directory.Delete(dir, true);
        }

        {
            Console.WriteLine();
            Console.WriteLine("== section-scoped keys ==");

            string dir = Path.Combine(Path.GetTempPath(), "crane-sect-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "TwoSections.ini");

            File.WriteAllLines(path, new string[]
            {
                "; @name Two Sections",
                "",
                "[Mod.Movement]",
                "; @param Speed.Full number default=110 min=50 max=200 label=\"Movement, Full\"",
                "Speed.Full=110",
                "",
                "[Mod.Attack]",
                "; @param Speed.Full number default=115 min=50 max=120 label=\"Attack, Full\"",
                "Speed.Full=115",
                ""
            });

            IniClient client = IniFile.Read(path);
            Check(client != null && client.Declared.Count == 2,
                  "the same key in two sections gives two settings");
            Check(client.Raw("Mod.Movement", "Speed.Full") == "110" &&
                  client.Raw("Mod.Attack", "Speed.Full") == "115",
                  "and each keeps its own value");

            IniSetting movement = IniFile.Find(client, "Mod.Movement", "Speed.Full");
            IniSetting attack = IniFile.Find(client, "Mod.Attack", "Speed.Full");
            Check(movement != null && attack != null, "both are found by section");
            Check(movement.Decl.Label == "Movement, Full" && attack.Decl.Label == "Attack, Full",
                  "a declaration binds to the section it sits in");
            Check(movement.Decl.Max == 200 && attack.Decl.Max == 120,
                  "including its own bounds");

            Check(IniFile.Write(path, "Mod.Attack", "Speed.Full", "118"),
                  "writing one section's copy succeeds");
            IniClient reread = IniFile.Read(path);
            Check(reread.Raw("Mod.Attack", "Speed.Full") == "118", "the named section changed");
            Check(reread.Raw("Mod.Movement", "Speed.Full") == "110",
                  "and the other section did NOT");

            Check(!IniFile.Write(path, "Mod.Nowhere", "Speed.Full", "1"),
                  "writing to a section that has no such key is refused");

            Check(IniClient.Slot("A", "BC") != IniClient.Slot("AB", "C"),
                  "section and key cannot run together into one identity");

            string explicitPath = Path.Combine(dir, "Explicit.ini");
            File.WriteAllLines(explicitPath, new string[]
            {
                "; @name Explicit",
                "; @param [Mod.Attack]Speed.Full number default=115 label=\"Attack\"",
                "; @enable [Mod.Core]Enabled",
                "",
                "[Mod.Core]", "Enabled=1",
                "[Mod.Attack]", "Speed.Full=115",
                ""
            });
            IniClient ex = IniFile.Read(explicitPath);
            Check(ex != null && IniFile.Find(ex, "Mod.Attack", "Speed.Full") != null,
                  "a bracketed section prefix binds the declaration");
            Check(ex != null && ex.EnableSection == "Mod.Core" && ex.HasEnable,
                  "and works for @enable too");

            Directory.Delete(dir, true);
        }

        {
            Console.WriteLine();
            Console.WriteLine("== steam launch detection ==");

            Check(GameLaunch.SteamUri == "steam://rungameid/3008130",
                  "the Steam URI names Dying Light: The Beast");

            Check(GameLaunch.IsSteamInstall(
                      @"D:\SteamLibrary\steamapps\common\Dying Light The Beast\ph_ft\work\bin\x64"),
                  "a path under steamapps is a Steam install");
            Check(GameLaunch.IsSteamInstall(@"C:\Games\steamapps\common\Whatever"),
                  "on any drive or library root");
            Check(GameLaunch.IsSteamInstall(@"D:\STEAMAPPS\common\Game"),
                  "case-insensitively, as Windows paths are");
            Check(GameLaunch.IsSteamInstall(@"D:\SteamLibrary\steamapps"),
                  "the steamapps directory itself counts");

            Check(!GameLaunch.IsSteamInstall(@"C:\Games\Dying Light The Beast\ph_ft\work\bin\x64"),
                  "a path outside steamapps is not");
            Check(!GameLaunch.IsSteamInstall(@"C:\steamappsxyz\common\Game"),
                  "and a directory merely starting with the word is not");
            Check(!GameLaunch.IsSteamInstall(""), "an empty folder is not");
            Check(!GameLaunch.IsSteamInstall(null), "and neither is null");

            string steamGame = @"D:\SteamLibrary\steamapps\common\Dying Light The Beast\ph_ft\work\bin\x64";
            string otherGame = @"C:\Games\Dying Light The Beast\ph_ft\work\bin\x64";

            Check(GameLaunch.Plan(steamGame, true) == GameLaunch.Route.DirectWithSteamEnv,
                  "a Steam copy with Steam running launches directly with the Steam environment");
            Check(GameLaunch.Plan(steamGame, false) == GameLaunch.Route.SteamUri,
                  "a Steam copy without Steam running defers to the Steam URI");
            Check(GameLaunch.Plan(otherGame, true) == GameLaunch.Route.Direct,
                  "a non-Steam copy launches directly even when Steam is running");
            Check(GameLaunch.Plan(otherGame, false) == GameLaunch.Route.Direct,
                  "and when it is not");

            System.Collections.Specialized.StringDictionary env =
                new System.Collections.Specialized.StringDictionary();
            GameLaunch.ApplySteamEnvironment(env);
            Check(env["SteamAppId"] == "3008130", "SteamAppId is set for the child process");
            Check(env["SteamGameId"] == "3008130", "and SteamGameId alongside it");
        }

        {
            Console.WriteLine();
            Console.WriteLine("== stale status reports ==");

            string dir = Path.Combine(Path.GetTempPath(), "crane-stale-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, StatusFile.FileName);
            File.WriteAllText(path, "{\"writes\":true,\"scripts\":[{\"file\":\"a.lua\",\"state\":\"loaded\"}]}");

            Check(StatusFile.IsFresh(dir), "a just-written report is fresh");
            StatusReport fresh = StatusFile.Read(dir);
            Check(fresh != null && fresh.StateOf("a.lua") == ScriptState.Loaded,
                  "and its states are read");

            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - StatusFile.Freshness - TimeSpan.FromMinutes(1));
            Check(!StatusFile.IsFresh(dir), "a report older than the window is not fresh");
            Check(StatusFile.Read(dir) == null, "and reads as no report at all");

            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - StatusFile.Freshness + TimeSpan.FromSeconds(30));
            Check(StatusFile.IsFresh(dir), "just inside the window is still fresh");

            Check(!StatusFile.IsFresh(Path.Combine(dir, "nope")),
                  "a folder with no report is not fresh");

            Directory.Delete(dir, true);
        }

        {
            Console.WriteLine();
            Console.WriteLine("== @param group and desc ==");

            string dir = Path.Combine(Path.GetTempPath(), "crane-params-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string script = Path.Combine(dir, "grouped.lua");

            File.WriteAllText(script, string.Join("\n", new string[]
            {
                "-- @name Grouped",
                "-- @param speed number default=1 min=0 max=9 label=\"Speed\" group=\"Timing\" desc=\"How fast it goes.\"",
                "-- @param loud bool default=false group=\"Timing\"",
                "-- @param colour string default=red group=\"Looks\"",
                "-- @param plain bool default=true",

                "-- @param bare bool default=false desc=Speeds looting up considerably",
                "-- @param bareg bool default=false group=Loot and Containers",
                ""
            }));

            string gname, gdesc;
            List<ParamDecl> declared;
            ScriptHeader.Read(script, out gname, out gdesc, out declared);

            Check(declared.Count == 6, "all six parameters parsed");
            ParamDecl speed = ScriptHeader.Find(declared, "speed");
            Check(speed != null && speed.Group == "Timing", "group is read");
            Check(speed != null && speed.Desc == "How fast it goes.", "desc is read");
            Check(speed != null && speed.Label == "Speed", "label still works alongside them");
            Check(speed != null && speed.HasMin && speed.HasMax, "and so do min/max");

            ParamDecl loud = ScriptHeader.Find(declared, "loud");
            Check(loud != null && loud.Group == "Timing", "a second parameter shares the group");
            Check(loud != null && loud.Desc == "", "an omitted desc is empty, not null");

            ParamDecl plain = ScriptHeader.Find(declared, "plain");
            Check(plain != null && plain.Group == "", "an ungrouped parameter has an empty group");

            ParamDecl bare = ScriptHeader.Find(declared, "bare");
            Check(bare != null && bare.Desc == "Speeds looting up considerably",
                  "an unquoted desc keeps every word");
            ParamDecl bareg = ScriptHeader.Find(declared, "bareg");
            Check(bareg != null && bareg.Group == "Loot and Containers",
                  "an unquoted group keeps every word");

            File.WriteAllText(script, string.Join("\n", new string[]
            {
                "-- @param x number desc=\"Tuned.\" min=2 max=8 group=\"G\"",
                ""
            }));
            ScriptHeader.Read(script, out gname, out gdesc, out declared);
            ParamDecl x = ScriptHeader.Find(declared, "x");
            Check(x != null && x.Desc == "Tuned.", "a quoted desc stops at the quote");
            Check(x != null && x.HasMin && x.Min == 2 && x.Max == 8,
                  "and the options after it still parse");
            Check(x != null && x.Group == "G", "including a later group");

            File.WriteAllText(script, "-- @param y bool default=true sparkle=yes group=\"G\"\n");
            ScriptHeader.Read(script, out gname, out gdesc, out declared);
            ParamDecl y = ScriptHeader.Find(declared, "y");
            Check(y != null, "an unknown option does not reject the parameter");
            Check(y != null && y.Group == "G", "and does not stop later options parsing");

            Directory.Delete(dir, true);
        }

        {
            Console.WriteLine();
            Console.WriteLine("== rows always have a label ==");

            ScriptEntry named = NewEntry("quick_hands.lua", true);
            named.Name = "Quick Hands";
            Check(new ScriptRow(named, null, false).Name == "Quick Hands",
                  "a named entry uses its name");
            Check(!new ScriptRow(named, null, false).Unnamed,
                  "a named entry is not flagged unnamed");

            ScriptEntry headerless = NewEntry("orphan.lua", false);
            ScriptRow headerlessRow = new ScriptRow(headerless, null, false);
            Check(headerlessRow.Name == "orphan.lua",
                  "an entry with no name falls back to its filename");
            Check(headerlessRow.Unnamed, "and is flagged unnamed");
            Check(headerlessRow.Secondary.Contains("report"),
                  "and its second line asks for a report rather than going blank");

            ScriptEntry empty = NewEntry("", false);
            Check(new ScriptRow(empty, null, false).Name.Length > 0,
                  "an entry with neither name nor file still renders a label");
            Check(new ScriptRow(empty, null, true).Name.Length > 0,
                  "the same holds for an unlisted row");
            Check(new ScriptRow(empty, null, false).Name !=
                  new ScriptRow(empty, null, true).Name,
                  "and the label says which side it came from");
            Check(new ScriptRow(empty, null, false).Secondary.Contains("filename"),
                  "the empty-file case is named as such in the second line");
        }

        Console.WriteLine();
        Console.WriteLine(_checks + " checks, " + _failures + " failure(s)");
        return _failures == 0 ? 0 : 1;
    }

    private static ScriptEntry NewEntry(string file, bool enabled)
    {
        ScriptEntry entry = new ScriptEntry();
        entry.File = file;
        entry.Enabled = enabled;
        return entry;
    }
}
