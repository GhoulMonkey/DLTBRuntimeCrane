// Creates the files Crane writes to, rather than shipping them.
//
// Why generate rather than package: Vortex owns every file it deploys, and a
// purge or redeploy can revert one. Crane 2.0.0 is the first mod here whose tool
// writes to its own files as normal operation -- the manifest changes on every
// checkbox tick, the INI whenever the writes toggle moves. Shipping them inside
// the archive puts a user's script list and tuned parameters at the mercy of a
// mod-manager operation that has nothing to do with them.
//
// So the archive carries only what is never written -- DLTBRuntimeCrane.asi,
// CraneManager.exe, the Lua licence -- and everything mutable is created here,
// on first run, in files Vortex has never heard of. What Vortex does to deployed
// config then stops mattering.
//
// The cost, and it is real: uninstalling through Vortex now leaves these behind,
// because Vortex only removes what it deployed. Arguably a feature -- settings
// survive a reinstall -- but it is litter, and the README says so.
//
// First run means "no manifest", not "no startup.lua". Deleting the example
// script is a choice the user made, and regenerating it on next launch would
// override it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CraneManager
{
    public static class FirstRun
    {
        // Ships the same warnings the packaged INI used to carry. Kept here
        // rather than in a resource so the text sits beside the code that has to
        // agree with it -- and so the test suite can assert the default.
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

        /*
         * There is no generated example script any more.
         *
         * The archive ships quick_hands.lua instead: a real mod, disabled until
         * ticked, which demonstrates parameters, groups, descriptions, six leases
         * and restoration on reload in a file the user can read. A generated
         * startup.lua taught the same header grammar less convincingly and had to
         * be maintained in a C# string literal.
         *
         * godmode.lua stays out of the archive. It remains in examples\ for
         * authors, but shipping a god-mode script alongside the platform invites
         * reading CRANE as a trainer, and that framing is best left to whoever
         * wants to build one.
         */

        public static bool IsNeeded(string folder)
        {
            return !File.Exists(Path.Combine(folder, "DLTBRuntimeCrane.manifest.json"));
        }

        // Creates only what is missing, and reports what it made so the window
        // can say so rather than silently writing to the user's game folder.
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

            /*
             * The generated script list is empty.
             *
             * Whatever .lua files the archive deployed are found by the window's
             * unlisted-script scan and shown ready to tick; they join the manifest
             * when the user enables one, which is the same path a hand-added
             * script takes. Writing them in here would mean the manifest lists
             * scripts nobody chose, and Remove would fight the next first run.
             */
            List<ScriptEntry> entries = new List<ScriptEntry>();
            ManifestFile.Save(Path.Combine(folder, "DLTBRuntimeCrane.manifest.json"), entries);
            created.Add("DLTBRuntimeCrane.manifest.json");
            return created;
        }
    }
}
