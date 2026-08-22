// SPDX-License-Identifier: GPL-3.0-only
// Round-trip and agreement tests for the manager's manifest handling.
//
// The property under test is that the manager and the runtime agree, rather than
// that the parser works on its own. If the manager writes a manifest
// DLTBRuntimeCrane.asi refuses, the failure surfaces in the game log rather than
// in the editor, where it could have been fixed.
//
// The case list below mirrors tools\test_manifest.c. The two must move together:
// if you add a case there, add it here. AgreementCases asserts the count so a
// one-sided edit is at least visible.

using System;
using System.Collections.Generic;
using System.IO;
using CraneLoader;

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

    // Mirrors the reject list in tools\test_manifest.c.
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

        // The round trip: anything the manager writes, the manager must read
        // back identically. This is what stops a name needing an escape, or an
        // empty list, from producing a file that will not load.
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

        // ---- parameters ------------------------------------------------
        // Same property as the agreement block above, applied to the schema's
        // one nested object: the manager must not write a params block the ASI
        // would refuse, nor accept one it would.
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
            // -0.9 is not representable exactly; "R" formatting is what keeps a
            // tuned number from drifting on every save.
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
            // An unlabelled parameter falls back to its own name rather than
            // rendering a blank row.
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

        // ---- generated defaults -----------------------------------------
        // package-all.sh used to assert AllowWrites=0 in the shipped INI. There
        // is no shipped INI any more, so the assertion moves here, to the text
        // that actually reaches a user's disk.
        Console.WriteLine();
        Console.WriteLine("generated defaults");
        {
            string ini = FirstRun.IniTemplate;
            Check(ini.Contains("AllowWrites=0"), "generated INI ships writes OFF");
            Check(!ini.Contains("AllowWrites=1"), "and never ships them on");
            Check(ini.Contains("[Crane]"), "has the section Crane reads");
            // The warning text is the only thing standing between a user and a
            // switch that can alter their save, so its absence is a failure.
            Check(ini.Contains("unsandboxed"), "keeps the warning about unsandboxed code");
            Check(ini.Contains("LogLevel"), "documents LogLevel too");

            // The example script is no longer generated; the archive ships
            // quick_hands.lua instead. What must stay true is that first run
            // writes an empty script list, so a deployed script is offered
            // rather than enabled on somebody's behalf.
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

        // ---- status file -------------------------------------------------
        // The property under test is that this reader accepts what Crane's C
        // writer emits. A Lua error routinely contains quotes, backslashes and
        // newlines, all of which the writer escapes -- so the escaping is the
        // interesting half, not the field names.
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
                "\n    { \"file\": \"gone.lua\", \"state\": \"missing\", \"error\": \"not found in scripts\\\\\" }," +
                "\n    { \"file\": \"reflex.lua\", \"state\": \"waiting\", \"error\": \"\" }" +
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
                // "waiting" must not read as Unknown. An older manager
                // does read it that way and shows a blank row, which is
                // degraded rather than wrong, but this one has to tell a
                // script held for a session from one the host has not
                // mentioned.
                Check(report.StateOf("reflex.lua") == ScriptState.Waiting, "waiting state");
                // Unknown must be distinguishable from disabled: one means Crane
                // had nothing to say, the other means it skipped the script.
                Check(report.StateOf("never_heard_of.lua") == ScriptState.Unknown,
                      "absent script reads as Unknown");

                string error = report.ErrorOf("quick_hands.lua");
                Check(error.Contains("attempt to call a nil value"), "error text survives");
                Check(error.Contains("\"claim\""), "escaped quotes are unescaped");
                Check(error.Contains("\n"), "escaped newline is unescaped");
                Check(report.ErrorOf("gone.lua").EndsWith("\\"), "escaped backslash survives");
                Check(report.ErrorOf("startup.lua") == "", "empty error stays empty");
            }

            // A half-written file must not take the window down; the watcher
            // will fire again when the write completes.
            File.WriteAllText(path, "{\n  \"version\": 1,\n  \"scripts\": [\n    { \"file\": \"a.lua\"");
            StatusReport partial = StatusFile.Read(dir);
            Check(partial != null, "a truncated report still returns something");

            File.Delete(path);
            Check(StatusFile.Read(dir) == null, "no file reads as no report");
            Directory.Delete(dir, true);
        }

        // ---- annotated client INIs ----
        //
        // The manager is a guest in these files. They belong to other mods, are
        // hand-edited by users, and their prose is the documentation somebody
        // relies on. So the property under test covers both "the right value
        // lands" and "nothing else moved".
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

            // Declared before any [Section] header, so they belong to the first
            // section the file goes on to open. That is the layout Time Lapse
            // uses and it must keep working.
            IniSetting factor = IniFile.Find(client, "Test", "Factor");
            Check(factor != null, "a declaration above the section header binds to it");
            Check(factor != null && factor.Decl.Group == "Speed",
                  "the Lua grammar's group= works here too");
            Check(factor != null && factor.Decl.Desc == "How fast.", "and desc=");

            Check(!client.Has("Test", "Missing"),
                  "a declared-but-absent key does not appear as a value");
            Check(!IniFile.Write(path, "Test", "Missing", "0"),
                  "and writing it is refused rather than appending a stray key");

            // Write one value, disturb nothing else.
            Check(IniFile.Write(path, "Test", "Factor", "250"), "a declared key can be written");
            string[] after = File.ReadAllLines(path);
            Check(after.Length == original.Length, "the line count is unchanged");
            Check(after[10] == "; Prose that must survive.", "comments are untouched");
            Check(after[8] == "[Test]", "the section header is untouched");
            Check(after[14] == "Unknown=leave me alone", "unrelated keys are untouched");
            Check(after[12].StartsWith("Factor  ="), "the key's own spacing is preserved");
            Check(IniFile.Read(path).Raw("Test", "Factor") == "250", "and the new value reads back");

            // Booleans are 1/0 in an INI, not true/false.
            Check(IniFile.FromBool(false) == "0", "false writes as 0");
            Check(IniFile.IsTruthy("1") && !IniFile.IsTruthy("0"), "and 1/0 read back");
            Check(IniFile.Write(path, "Test", "Enabled", "0"), "the enable key can be written");
            Check(!IniFile.Read(path).Enabled, "which turns the client off");

            // Whole numbers must not acquire a decimal point: these are key
            // codes and percentages, and the prose beside them says "119".
            Check(IniFile.ToRaw(factor.Decl, 119.0) == "119",
                  "a whole number writes without a decimal point");

            // An unannotated INI is not a client.
            string plain = Path.Combine(dir, "Plain.ini");
            File.WriteAllLines(plain, new string[] { "[X]", "Key=1" });
            Check(IniFile.Read(plain) == null, "an unannotated INI is not listed as a client");

            Directory.Delete(dir, true);
        }

        // ---- the platform's own INI is annotated too ----
        //
        // CRANE's INI is generated from FirstRun.IniTemplate rather than shipped,
        // so a typo in those annotations reaches users without any build step
        // touching it. This is the only thing that reads the template the way the
        // manager will.
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

            // The shipped default is the whole safety story; build-asi.sh refuses
            // to build if it is 1, and this asserts the same thing through the
            // reader the manager actually uses.
            Check(self != null && self.Raw("Crane", "AllowWrites") == "0",
                  "and ships OFF");

            // No @enable: CRANE has no enable key of its own, and inventing one
            // would put a checkbox on the platform itself.
            Check(self != null && !self.HasEnable, "the host declares no enable key");

            Directory.Delete(dir, true);
        }

        // ---- the same key in two sections ----
        //
        // This is the shape Beastly Hunger actually ships: Speed.Full under both
        // [BeastlyHunger.MovementSpeed] and [BeastlyHunger.MeleeAttackSpeed],
        // and the same for Damage.* and Penalty.*. The first version of this
        // reader keyed on bare names and refused repeats as "ambiguous", which
        // made fifteen of that mod's settings unmanageable. Repeating a name
        // across sections is what sections are for.
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

            // The write must land in the named section and nowhere else.
            Check(IniFile.Write(path, "Mod.Attack", "Speed.Full", "118"),
                  "writing one section's copy succeeds");
            IniClient reread = IniFile.Read(path);
            Check(reread.Raw("Mod.Attack", "Speed.Full") == "118", "the named section changed");
            Check(reread.Raw("Mod.Movement", "Speed.Full") == "110",
                  "and the other section did NOT");

            Check(!IniFile.Write(path, "Mod.Nowhere", "Speed.Full", "1"),
                  "writing to a section that has no such key is refused");

            // Section names cannot bleed into each other through the composite
            // key: [A] BC and [AB] C must stay distinct.
            Check(IniClient.Slot("A", "BC") != IniClient.Slot("AB", "C"),
                  "section and key cannot run together into one identity");

            // The explicit form, for INIs that gather declarations in one block.
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

        // ---- launching through Steam ----
        //
        // Running the game exe directly stops at "Steam client application is
        // required in order to play the game": it is DRM-wrapped and expects to
        // have been started by Steam. Which route to take is decided by where
        // the install sits, and getting that wrong fails silently in one
        // direction -- steam://rungameid on a copy Steam does not know about
        // produces no error and no game.
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

            // Which route, given where the game is and whether Steam is up.
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

        // ---- a stale status report is not a report ----
        //
        // Crane cannot write this file when the game is not running, so the file
        // outliving the session is the normal case, not an edge case. Reading it
        // without checking its age put a green "loaded" dot beside every script
        // from the last session while the header bar correctly said the game was
        // not running -- the window contradicting itself, in the reassuring
        // direction.
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

            // Older than the window: the game has exited.
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - StatusFile.Freshness - TimeSpan.FromMinutes(1));
            Check(!StatusFile.IsFresh(dir), "a report older than the window is not fresh");
            Check(StatusFile.Read(dir) == null, "and reads as no report at all");

            // The boundary, from the safe side.
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - StatusFile.Freshness + TimeSpan.FromSeconds(30));
            Check(StatusFile.IsFresh(dir), "just inside the window is still fresh");

            Check(!StatusFile.IsFresh(Path.Combine(dir, "nope")),
                  "a folder with no report is not fresh");

            // The report is judged against the game's start time rather than its
            // age. Crane writes the status file from reload_scripts and nowhere
            // else, so an untouched session's report ages past any window while
            // still describing that session correctly.
            DateTime launched = DateTime.UtcNow - TimeSpan.FromHours(2);
            File.SetLastWriteTimeUtc(path, launched + TimeSpan.FromSeconds(20));

            Check(!StatusFile.IsFresh(dir),
                  "a two-hour-old report fails the age window, as it always did");
            Check(StatusFile.DescribesCurrentSession(dir, launched),
                  "but it describes the session that started before it was written");
            StatusReport current = StatusFile.Read(dir, launched);
            Check(current != null && current.StateOf("a.lua") == ScriptState.Loaded,
                  "so its states are read however old the file is");

            // The case the age window existed for. A relaunch invalidates the
            // previous report at once rather than five minutes later.
            DateTime relaunched = DateTime.UtcNow - TimeSpan.FromSeconds(5);
            File.SetLastWriteTimeUtc(path, relaunched - TimeSpan.FromSeconds(1));
            Check(StatusFile.IsFresh(dir), "a report written a moment ago passes the age window");
            Check(!StatusFile.DescribesCurrentSession(dir, relaunched),
                  "but not if this run of the game started after it was written");
            Check(StatusFile.Read(dir, relaunched) == null,
                  "and it reads as no report rather than as the previous session's");

            // An unreadable start time -- the game elevated, the tool not --
            // falls back to the age window rather than to "not connected".
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            Check(StatusFile.DescribesCurrentSession(dir, null),
                  "with no start time available, a recent report is still believed");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - StatusFile.Freshness - TimeSpan.FromMinutes(1));
            Check(!StatusFile.DescribesCurrentSession(dir, null),
                  "and an old one is not");

            // The reason shown in the capsule's tooltip. It has to be accurate
            // in the states where the verdict is surprising, and it has to keep
            // agreeing with the verdict, which is why one call decides both.
            File.SetLastWriteTimeUtc(path, launched + TimeSpan.FromSeconds(20));

            StatusFile.SessionVerdict attached = StatusFile.Judge(dir, true, launched);
            Check(attached.Current, "a two-hour-old report from this session is current");
            Check(attached.Reason.Contains("after it"),
                  "and the reason says the report came after the game started");
            Check(attached.Reason.Contains("age is not evidence"),
                  "and addresses the age directly");

            StatusFile.SessionVerdict previous = StatusFile.Judge(dir, true, relaunched);
            Check(!previous.Current, "a report from before this run is not current");
            Check(previous.Reason.Contains("earlier session") &&
                  previous.Reason.Contains("Waiting"),
                  "and the reason says whose report it is and what happens next");

            StatusFile.SessionVerdict offline = StatusFile.Judge(dir, false, null);
            Check(!offline.Current, "with the game down, nothing is current");
            Check(offline.Reason.Contains("not running"), "and the reason says so first");

            StatusFile.SessionVerdict noReport =
                StatusFile.Judge(Path.Combine(dir, "nope"), true, launched);
            Check(!noReport.Current, "a folder with no report is not current");
            Check(noReport.Reason.Contains("has not written"),
                  "and the reason distinguishes never-written from written-earlier");

            // Which of the two tests produced a verdict is invisible from
            // outside, since both render the same two words, so the fallback
            // names itself.
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow - TimeSpan.FromMinutes(1));
            StatusFile.SessionVerdict fallback = StatusFile.Judge(dir, true, null);
            Check(fallback.Current, "with no start time, a recent report still counts");
            Check(fallback.Reason.Contains("start time could not be read"),
                  "and the reason says the start-time test was unavailable");
            Check(fallback.Reason.Contains("elevated"),
                  "names the case that produces it");
            Check(fallback.Reason.Contains("unreliable"),
                  "and does not present the fallback as trustworthy");

            foreach (DateTime? start in new DateTime?[] { launched, relaunched, null })
                Check(StatusFile.Judge(dir, true, start).Current ==
                      StatusFile.DescribesCurrentSession(dir, start),
                      "Judge and DescribesCurrentSession agree");

            Directory.Delete(dir, true);
        }

        // ---- @param group= and desc= ----
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
                // The trap: prose without quotes. Silently became "Speeds"
                // before Tokenize started recording quoting.
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

            // Why Tokenize tracks quoting.
            ParamDecl bare = ScriptHeader.Find(declared, "bare");
            Check(bare != null && bare.Desc == "Speeds looting up considerably",
                  "an unquoted desc keeps every word");
            ParamDecl bareg = ScriptHeader.Find(declared, "bareg");
            Check(bareg != null && bareg.Group == "Loot and Containers",
                  "an unquoted group keeps every word");

            // A quoted value must not swallow the options after it.
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

            // Unknown options are still ignored rather than failing the line --
            // an older manager must not choke on a newer script.
            File.WriteAllText(script, "-- @param y bool default=true sparkle=yes group=\"G\"\n");
            ScriptHeader.Read(script, out gname, out gdesc, out declared);
            ParamDecl y = ScriptHeader.Find(declared, "y");
            Check(y != null, "an unknown option does not reject the parameter");
            Check(y != null && y.Group == "G", "and does not stop later options parsing");

            Directory.Delete(dir, true);
        }

        // ---- @claims, and the conflict it predicts ----
        //
        // The property under test is the one the UI states: the script the
        // manifest loads first keeps the path. Reversed, the window would tell
        // the user to change the script that was working.
        {
            Console.WriteLine();
            Console.WriteLine("== @claims parsing ==");

            string dir = Path.Combine(Path.GetTempPath(), "crane-claims-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string script = Path.Combine(dir, "claimer.lua");

            File.WriteAllText(script, string.Join("\n", new string[]
            {
                "-- @name Claimer",
                "-- @param body_containers bool default=true label=\"Bodies\"",
                "-- @claims var.DamageMulAll",
                "-- @claims interaction.BodyContainer.duration_scale when=body_containers",
                "-- @claims var.DamageMulAll",
                "-- @claims",
                ""
            }));

            string cname, cdesc;
            List<ParamDecl> cdeclared;
            List<ClaimDecl> claims;
            ScriptHeader.Read(script, out cname, out cdesc, out cdeclared, out claims);

            Check(claims.Count == 2, "two distinct paths, the repeat and the empty line dropped");
            ClaimDecl damage = ScriptHeader.FindClaim(claims, "var.DamageMulAll");
            Check(damage != null && damage.When == "", "an ungated claim has an empty gate");
            ClaimDecl gated = ScriptHeader.FindClaim(claims, "interaction.BodyContainer.duration_scale");
            Check(gated != null && gated.When == "body_containers", "when= is read");
            Check(cdeclared.Count == 1, "@param still parses alongside @claims");

            // A script that declares nothing produces an empty list rather than
            // null; every caller walks it unconditionally.
            File.WriteAllText(script, "-- @name Silent\n");
            ScriptHeader.Read(script, out cname, out cdesc, out cdeclared, out claims);
            Check(claims != null && claims.Count == 0, "a script with no @claims yields an empty list");

            // Past the 60-line limit the header scan used before.
            string[] padded = new string[80];
            padded[0] = "-- @name Deep";
            for (int i = 1; i < 79; i++) padded[i] = "-- filler";
            padded[79] = "-- @claims var.Deep";
            File.WriteAllText(script, string.Join("\n", padded));
            ScriptHeader.Read(script, out cname, out cdesc, out cdeclared, out claims);
            Check(ScriptHeader.FindClaim(claims, "var.Deep") != null,
                  "a declaration below line 60 is still read");

            Console.WriteLine();
            Console.WriteLine("== claim conflicts ==");

            ScriptEntry quick = NewEntry("quick_hands.lua", true);
            quick.Name = "Quick Hands";
            quick.Claims.Add(NewClaim("interaction.BodyContainer.duration_scale", ""));
            quick.Claims.Add(NewClaim("interaction.Container.duration_scale", ""));

            ScriptEntry steady = NewEntry("steady_hands.lua", true);
            steady.Name = "Steady Hands";
            steady.Claims.Add(NewClaim("interaction.BodyContainer.duration_scale", ""));
            steady.Claims.Add(NewClaim("interaction.Container.duration_scale", ""));

            List<ScriptEntry> both = new List<ScriptEntry>();
            both.Add(quick);
            both.Add(steady);
            ClaimMap.Compute(both);

            Check(!quick.Conflict.Refused, "the script that loads first is not refused");
            Check(quick.Conflict.Won.Count == 2, "and is told what it is taking");
            Check(steady.Conflict.Refused, "the script that loads second is refused");
            Check(steady.Conflict.Silenced, "and with every path gone, is silenced");
            Check(steady.Conflict.Rival == "Quick Hands", "the rival is named");
            Check(steady.Conflict.RivalFile == "quick_hands.lua", "and identified by file");

            // Reordering swaps the verdict, which is what the "Run this one
            // instead" button does to the manifest.
            both.Reverse();
            ClaimMap.Compute(both);
            Check(quick.Conflict.Refused && !steady.Conflict.Refused,
                  "reordering the manifest swaps who wins");

            // A disabled script claims nothing, so unticking one side clears the
            // other's warning.
            quick.Enabled = false;
            ClaimMap.Compute(both);
            Check(!steady.Conflict.Refused && !quick.Conflict.Refused,
                  "disabling one side clears the conflict entirely");
            quick.Enabled = true;

            // A missing file never runs, so it takes no path from anything.
            both.Reverse();
            quick.Missing = true;
            ClaimMap.Compute(both);
            Check(!steady.Conflict.Refused, "a script missing from \\scripts holds nothing");
            quick.Missing = false;

            // Partial overlap counts paths lost, not scripts.
            steady.Claims.Add(NewClaim("var.SomethingElse", ""));
            ClaimMap.Compute(both);
            Check(steady.Conflict.Wanted == 3, "every active claim is counted as wanted");
            Check(steady.Conflict.Lost.Count == 2, "only the overlapping ones are lost");
            Check(!steady.Conflict.Silenced, "a partly refused script is not silenced");

            // A claim gated off is not a claim.
            steady.Claims.Clear();
            steady.Claims.Add(NewClaim("interaction.Container.duration_scale", "containers"));
            steady.Params["containers"] = false;
            ClaimMap.Compute(both);
            Check(!steady.Conflict.Refused, "a claim gated off by a bool is not made");
            Check(steady.Conflict.Wanted == 0, "and is not counted as wanted");

            steady.Params["containers"] = true;
            ClaimMap.Compute(both);
            Check(steady.Conflict.Refused, "and is made again when the gate is on");

            steady.Params["containers"] = 0.0;
            ClaimMap.Compute(both);
            Check(!steady.Conflict.Refused, "a numeric gate at zero is off");
            steady.Params["containers"] = 2.5;
            ClaimMap.Compute(both);
            Check(steady.Conflict.Refused, "and a non-zero number is on");

            // With no stored value the gate reads the declared default, so a
            // freshly installed script is still checked.
            steady.Params.Remove("containers");
            ClaimMap.Compute(both);
            Check(steady.Conflict.Refused, "an unstored gate with no declaration counts as on");

            ParamDecl containersOff = new ParamDecl();
            containersOff.Key = "containers";
            containersOff.Type = ParamType.Bool;
            containersOff.Default = false;
            steady.Declared.Add(containersOff);
            ClaimMap.Compute(both);
            Check(!steady.Conflict.Refused, "an unstored gate defaulting to false counts as off");

            // off= names the neutral value, which for a multiplier is 1.
            steady.Declared.Clear();
            steady.Claims.Clear();
            steady.Claims.Add(NewGatedClaim("interaction.Container.duration_scale",
                                            "strength", "1"));
            steady.Params.Remove("containers");
            steady.Params["strength"] = 1.0;
            ClaimMap.Compute(both);
            Check(!steady.Conflict.Refused, "a claim at its declared off= value is not made");
            steady.Params["strength"] = 2.0;
            ClaimMap.Compute(both);
            Check(steady.Conflict.Refused, "and is made at any other value");
            steady.Params["strength"] = 0.0;
            ClaimMap.Compute(both);
            Check(steady.Conflict.Refused, "off= replaces the zero rule rather than adding to it");

            {
                string offScript = Path.Combine(dir, "off.lua");
                File.WriteAllText(offScript, "-- @claims var.X when=k off=1.5\n");
                List<ClaimDecl> offClaims;
                ScriptHeader.Read(offScript, out cname, out cdesc, out cdeclared, out offClaims);
                ClaimDecl only = ScriptHeader.FindClaim(offClaims, "var.X");
                Check(only != null && only.When == "k" && only.Off == "1.5",
                      "when= and off= parse together");
            }

            // fallback= and needs=: a refusal the script already handles. Well
            // Fed loses var.DamageMulAll to Fight or Flight and still works, by
            // routing through Damage Budget.
            {
                string fallbackScript = Path.Combine(dir, "fallback.lua");
                File.WriteAllText(fallbackScript,
                    "-- @claims var.DamageMulAll fallback=\"contributes through Damage Budget\" " +
                    "needs=damage_budget.lua\n" +
                    "-- @claims var.Other fallback=routes around it unquoted and entire\n");
                List<ClaimDecl> parsed;
                ScriptHeader.Read(fallbackScript, out cname, out cdesc, out cdeclared, out parsed);
                ClaimDecl withNeeds = ScriptHeader.FindClaim(parsed, "var.DamageMulAll");
                Check(withNeeds != null && withNeeds.Fallback == "contributes through Damage Budget",
                      "a quoted fallback stops at the quote");
                Check(withNeeds != null && withNeeds.Needs == "damage_budget.lua",
                      "and the option after it still parses");
                ClaimDecl unquoted = ScriptHeader.FindClaim(parsed, "var.Other");
                Check(unquoted != null && unquoted.Fallback == "routes around it unquoted and entire",
                      "an unquoted fallback keeps every word");

                ScriptEntry holder = NewEntry("holder.lua", true);
                holder.Name = "Fight or Flight";
                holder.Claims.Add(NewClaim("var.DamageMulAll", ""));

                ScriptEntry fed = NewEntry("well_fed.lua", true);
                fed.Name = "Well Fed";
                ClaimDecl covered = NewClaim("var.DamageMulAll", "");
                covered.Fallback = "contributes through Damage Budget instead";
                covered.Needs = "damage_budget.lua";
                fed.Claims.Add(covered);

                ScriptEntry budget = NewEntry("damage_budget.lua", true);
                budget.Name = "Damage Budget";

                List<ScriptEntry> three = new List<ScriptEntry>();
                three.Add(holder);
                three.Add(fed);
                three.Add(budget);
                ClaimMap.Compute(three);
                Check(fed.Conflict.Refused, "the refusal is still reported");
                Check(fed.Conflict.Covered, "but it is covered");
                Check(!fed.Conflict.Silenced, "so the script is not called silent");
                Check(!new ScriptRow(fed, null, false).HasConflict,
                      "and no amber dot is raised over a refusal its author handles");
                Check(new ScriptRow(fed, null, false).Secondary.Contains("Damage Budget"),
                      "the second line still says what it does instead");

                // With the arbiter disabled there is no alternative route.
                budget.Enabled = false;
                ClaimMap.Compute(three);
                Check(fed.Conflict.Silenced,
                      "a fallback whose needs= script is disabled does not count");
                Check(new ScriptRow(fed, null, false).HasConflict,
                      "and the warning comes back");

                three.Remove(budget);
                ClaimMap.Compute(three);
                Check(fed.Conflict.Silenced, "nor does one naming a script that is not there");
            }

            // And the row reports it in the words the user reads.
            steady.Declared.Clear();
            steady.Claims.Clear();
            steady.Claims.Add(NewClaim("interaction.Container.duration_scale", ""));
            ClaimMap.Compute(both);
            ScriptRow steadyRow = new ScriptRow(steady, null, false);
            Check(steadyRow.HasConflict, "the losing row carries the conflict marker");
            Check(steadyRow.Secondary.Contains("Quick Hands"),
                  "and its second line names the script beating it");
            Check(!new ScriptRow(quick, null, false).HasConflict,
                  "the winning row does not, so one problem shows one warning");

            Directory.Delete(dir, true);
        }

        /*
         * ---- the load order ----
         *
         * The list shows load order, and a row can be dragged to change it. The
         * gesture cannot be tested here; the arithmetic behind it can, and the
         * arithmetic is where the off-by-one lives: a target below the moving
         * row is one place too far once that row has been taken out.
         */
        Console.WriteLine();
        Console.WriteLine("load order");
        {
            List<ScriptEntry> order = new List<ScriptEntry>();
            order.Add(NewEntry("a.lua", true));
            order.Add(NewEntry("b.lua", true));
            order.Add(NewEntry("c.lua", true));
            order.Add(NewEntry("d.lua", true));
            ScriptEntry a = order[0], b = order[1], c = order[2], d = order[3];

            // Downwards. Dropping `a` on the lower edge of `c` is target 3, and
            // the answer is a-out, insert-at-2: b, c, a, d. Target 3 taken
            // literally would give b, c, d, a, one row too far.
            Check(LoadOrder.Move(order, a, 3), "a move downwards is a change");
            Check(order[0] == b && order[1] == c && order[2] == a && order[3] == d,
                  "dropping below lands under the row that was targeted, not past it");

            // Upwards needs no adjustment: nothing before the target has moved.
            Check(LoadOrder.Move(order, a, 0), "a move upwards is a change");
            Check(order[0] == a && order[1] == b && order[2] == c && order[3] == d,
                  "dropping above lands where the line was drawn");

            // The two no-ops. One pixel of mouse movement separates them, and
            // both leave the manifest alone.
            Check(!LoadOrder.Move(order, b, 1), "dropping a row on its own top edge changes nothing");
            Check(!LoadOrder.Move(order, b, 2), "dropping a row on its own bottom edge changes nothing");
            Check(order[1] == b, "and neither one moved it");

            // Past the end: the count is a valid target, meaning "last".
            Check(LoadOrder.Move(order, a, order.Count), "dropping past the last row is a change");
            Check(order[3] == a, "and puts the script last in the load order");
            Check(LoadOrder.PositionOf(order, a) == 3, "the reported position is the new one");

            // Out of range and unknown entries are refused rather than throwing;
            // both are reachable from a drop resolved against a stale row.
            Check(!LoadOrder.Move(order, a, order.Count + 1), "a target past the end is refused");
            Check(!LoadOrder.Move(order, a, -1), "a negative target is refused");
            Check(!LoadOrder.Move(order, NewEntry("gone.lua", true), 0),
                  "moving an entry the list does not hold is refused");
            Check(order.Count == 4, "and no refusal changed the list");

            // What the buttons ask for, in the pre-removal form MoveSelected
            // hands over: one step later is target index+2, one step earlier is
            // target index-1.
            List<ScriptEntry> steps = new List<ScriptEntry>();
            steps.Add(NewEntry("a.lua", true));
            steps.Add(NewEntry("b.lua", true));
            steps.Add(NewEntry("c.lua", true));
            ScriptEntry first = steps[0];
            Check(LoadOrder.Move(steps, first, 2) && steps[1] == first,
                  "Load later moves the script exactly one place");
            Check(LoadOrder.Move(steps, first, 0) && steps[0] == first,
                  "Load earlier moves it exactly one place back");
        }

        /*
         * ---- enabled above disabled ----
         *
         * The list keeps every enabled script above every disabled one, and the
         * tick maintains it: enabling puts a script at the back of the enabled
         * run, disabling puts it at the front of the disabled one.
         *
         * The second half is the one easy to omit. An enable-only rule looks
         * correct while the list is built up, and breaks the first time a script
         * in the middle is switched off.
         */
        Console.WriteLine();
        Console.WriteLine("enabled above disabled");
        {
            List<ScriptEntry> list = new List<ScriptEntry>();
            list.Add(NewEntry("a.lua", true));
            list.Add(NewEntry("b.lua", true));
            list.Add(NewEntry("c.lua", false));
            list.Add(NewEntry("d.lua", false));
            ScriptEntry a = list[0], b = list[1], c = list[2], d = list[3];

            Check(LoadOrder.EnabledCount(list) == 2, "the enabled run is counted");
            Check(LoadOrder.Boundary(list, null) == 2, "and the boundary sits just past it");

            /*
             * Count and boundary are different numbers, and the manager needs
             * the boundary wherever it needs an index. They agree only while the
             * enabled entries start at the top; a manifest with disabled scripts
             * above the run made the detail pane report a position past its own
             * total.
             */
            {
                List<ScriptEntry> offset = new List<ScriptEntry>();
                offset.Add(NewEntry("off1.lua", false));
                offset.Add(NewEntry("off2.lua", false));
                offset.Add(NewEntry("on1.lua", true));
                offset.Add(NewEntry("on2.lua", true));
                Check(LoadOrder.EnabledCount(offset) == 2, "two entries are enabled");
                Check(LoadOrder.Boundary(offset, null) == 4,
                      "but the run ends at index 4, not index 2");
                Check(LoadOrder.Boundary(offset, null) - 1 == 3,
                      "so the last orderable position is the last enabled entry");
                ScriptEntry moved = offset[2];
                Check(LoadOrder.Move(offset, moved, LoadOrder.Boundary(offset, null)),
                      "dropping past the run is a change");
                Check(offset[3] == moved, "and lands last among the enabled");
            }

            // Enabling the last script moves it to the back of the enabled run
            // rather than leaving it below an already-disabled one.
            d.Enabled = true;
            Check(LoadOrder.Regroup(list, d), "enabling a script below the run moves it");
            Check(list[0] == a && list[1] == b && list[2] == d && list[3] == c,
                  "a newly enabled script lands last among the enabled");

            // Disabling from the middle pushes the script down, or the
            // arrangement lasts one action.
            a.Enabled = false;
            Check(LoadOrder.Regroup(list, a), "disabling from inside the run moves it");
            Check(list[0] == b && list[1] == d && list[2] == a && list[3] == c,
                  "a newly disabled script lands first among the disabled");

            // Already in the right place: no move, and so no manifest write.
            Check(!LoadOrder.Regroup(list, b), "an enabled script already in the run stays put");
            Check(!LoadOrder.Regroup(list, c), "and a disabled script already below it stays put");

            // An enabled script partway up the run is where it belongs. Moving
            // it to the back would reorder a list the user had arranged, as a
            // side effect of a tick elsewhere.
            List<ScriptEntry> run = new List<ScriptEntry>();
            run.Add(NewEntry("p.lua", true));
            run.Add(NewEntry("q.lua", true));
            run.Add(NewEntry("r.lua", true));
            ScriptEntry q = run[1];
            Check(!LoadOrder.Regroup(run, q), "a script in the middle of the run is not hoisted");
            Check(run[1] == q, "and does not move");

            /*
             * A hand-edited manifest can interleave them, and Regroup does not
             * sort: it moves the one entry whose tick changed and leaves the
             * rest alone. Boundary reads the list as it is, which is why it
             * scans for the last enabled entry rather than counting.
             */
            List<ScriptEntry> messy = new List<ScriptEntry>();
            messy.Add(NewEntry("on1.lua", true));
            messy.Add(NewEntry("off1.lua", false));
            messy.Add(NewEntry("on2.lua", true));
            Check(LoadOrder.Boundary(messy, null) == 3,
                  "the boundary follows the last enabled entry, not the first gap");
            ScriptEntry stray = messy[1];
            stray.Enabled = true;
            Check(!LoadOrder.Regroup(messy, stray),
                  "ticking the interleaved entry needs no move: the run is now unbroken");
            Check(messy[1] == stray, "and it stayed where it was");

            // Nothing enabled: the boundary is the top, so the first script
            // ticked becomes the first to load.
            List<ScriptEntry> none = new List<ScriptEntry>();
            none.Add(NewEntry("x.lua", false));
            none.Add(NewEntry("y.lua", false));
            Check(LoadOrder.Boundary(none, null) == 0, "with nothing enabled the boundary is the top");
            ScriptEntry y = none[1];
            y.Enabled = true;
            Check(LoadOrder.Regroup(none, y) && none[0] == y,
                  "the first script enabled goes to the top");
        }

        // ---- the phantom-row invariant ----
        //
        // A row must always have a visible label, whatever state its entry is
        // in. The bug this guards is a checkbox with nothing beside it: not
        // merely ugly but unreportable, which is why it outlived two attempts
        // at diagnosis. These cases construct the states the rest of the code
        // says are impossible.
        {
            Console.WriteLine();
            Console.WriteLine("== rows always have a label ==");

            ScriptEntry named = NewEntry("quick_hands.lua", true);
            named.Name = "Quick Hands";
            Check(new ScriptRow(named, null, false).Name == "Quick Hands",
                  "a named entry uses its name");
            Check(!new ScriptRow(named, null, false).Unnamed,
                  "a named entry is not flagged unnamed");

            // ScriptHeader.Read cannot leave this empty today. It is still the
            // state to survive, because "cannot happen" is what the last two
            // diagnoses assumed.
            ScriptEntry headerless = NewEntry("orphan.lua", false);
            ScriptRow headerlessRow = new ScriptRow(headerless, null, false);
            Check(headerlessRow.Name == "orphan.lua",
                  "an entry with no name falls back to its filename");
            Check(headerlessRow.Unnamed, "and is flagged unnamed");
            Check(headerlessRow.Secondary.Contains("report"),
                  "and its second line asks for a report rather than going blank");

            // The worst case: nothing to fall back to at all.
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

    private static ClaimDecl NewClaim(string path, string when)
    {
        ClaimDecl claim = new ClaimDecl();
        claim.Path = path;
        claim.When = when;
        return claim;
    }

    private static ClaimDecl NewGatedClaim(string path, string when, string off)
    {
        ClaimDecl claim = NewClaim(path, when);
        claim.Off = off;
        return claim;
    }

    private static ScriptEntry NewEntry(string file, bool enabled)
    {
        ScriptEntry entry = new ScriptEntry();
        entry.File = file;
        entry.Enabled = enabled;
        return entry;
    }
}
