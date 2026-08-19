using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CraneManager
{
    public static class FirstRun
    {
        public const string IniTemplate =
@"; Crane -- optional developer client for DLTBRuntimeBridge.
;
; Created by CraneManager, and deliberately NOT shipped in the mod archive:
; a file Vortex deploys is a file Vortex can revert, and this one holds a
; setting you chose. Delete it and Crane behaves as though every default
; were set, which means it observes and changes nothing.

[Crane]

; ---------------------------------------------------------------------------
; Declarations for CraneManager, which reads them to build its Settings window.
; Comments only; Crane itself ignores them.
;
; @name CRANE
; @description The Lua scripting host. These are its own settings.
; @param AllowWrites bool default=false label=""Allow scripts to change the game"" group=""Safety"" desc=""Off, scripts may only read. On, enabled scripts can write game state, hold leases and change a save you care about. Takes effect without restarting.""
; ---------------------------------------------------------------------------

; How much Crane records, in the shared Bridge console and log.
;   1 = Info   normal lifecycle and significant operational events
;   2 = Debug  which scripts loaded, what they claimed, what was released
;   3 = Trace  fine-grained execution detail
; Warnings and errors are recorded at every level.
; Leave unset to follow the Bridge's own level.
;LogLevel=1

; Allow scripts to CHANGE GAME STATE.
;
; Default 0: scripts may read, describe, list and observe events, and every
; write is refused with CRANE_WRITES_DISABLED. That is the safe setting for
; normal play.
;
; Set to 1 and enabled scripts can write state paths, hold exclusive leases
; and contribute modifiers -- including on a save you care about. Lua scripts
; are unsandboxed code that you or someone else wrote; this switch is the only
; thing standing between a bad line and your game state.
;
; It takes effect without restarting the game. Turning it off releases
; everything scripts are holding and restores what they changed.
;
; CraneManager can set this for you, under Settings.
AllowWrites=0
";

        public static bool IsNeeded(string folder)
        {
            return !File.Exists(Path.Combine(folder, "DLTBRuntimeCrane.manifest.json"));
        }

        public static List<string> Create(string folder)
        {
            List<string> created = new List<string>();
            string scripts = Path.Combine(folder, "scripts");
            Directory.CreateDirectory(scripts);

            string ini = Path.Combine(folder, "DLTBRuntimeCrane.ini");
            if (!File.Exists(ini))
            {
                File.WriteAllText(ini, IniTemplate, new UTF8Encoding(false));
                created.Add("DLTBRuntimeCrane.ini");
            }

            List<ScriptEntry> entries = new List<ScriptEntry>();
            ManifestFile.Save(Path.Combine(folder, "DLTBRuntimeCrane.manifest.json"), entries);
            created.Add("DLTBRuntimeCrane.manifest.json");
            return created;
        }
    }
}
