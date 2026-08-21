// SPDX-License-Identifier: GPL-3.0-only
// Works out, before the game is started, which enabled scripts will lose a
// lease to which other enabled script.
//
// The problem this exists for: leases are exclusive per path, so ticking Steady
// Hands while Quick Hands is already on produces a script that is enabled,
// loads cleanly, reports no error, and does nothing. Every signal the window had
// said it was fine -- the checkbox was ticked and the dot was green, because
// loading succeeded; only the claim inside it was refused. The user's evidence
// that anything was wrong was that looting felt like vanilla.
//
// Computed here rather than read back from the runtime.
// The runtime knows the truth but only once the game is running, which is after
// the user has finished configuring, quit the window, launched, played, and
// noticed. A warning that arrives then has already cost the trip. This answer is
// available at the moment the checkbox is ticked, from declarations parsed out
// of the script headers -- see ClaimDecl for why they are declared rather than
// discovered, and for what a missing declaration does and does not cost.
//
// The rule being modelled is Crane's own: the manifest is walked in order and
// the first enabled script to claim a path holds it, so an entry higher in the
// manifest beats a lower one. That is the same rule the Move up / Move down
// buttons exist to control, which is why this file and those buttons have to
// keep saying the same thing.

using System;
using System.Collections.Generic;
using System.Globalization;

namespace CraneLoader
{
    // One path two enabled scripts both want, seen from one side of the fight.
    public class ClaimClash
    {
        public string Path { get; set; }
        public string OtherFile { get; set; }
        public string OtherName { get; set; }
        // True when this script is the one that gets the path -- it sits higher
        // in the manifest -- and the other is refused.
        public bool ThisWins { get; set; }
        // What the refused script does instead, if it declared anything. Copied
        // from the losing side's own declaration, so on a won clash it describes
        // what the OTHER script falls back to.
        public string Fallback { get; set; }

        public ClaimClash()
        {
            Path = "";
            OtherFile = "";
            OtherName = "";
            Fallback = "";
        }
    }

    public class ScriptConflict
    {
        // How many paths this script asks for at its current settings. Kept so
        // the UI can say "all six" rather than "some", which is the difference
        // between a script that is dead and one that is partly working.
        public int Wanted { get; set; }
        public List<ClaimClash> Lost { get; set; }
        public List<ClaimClash> Won { get; set; }

        public ScriptConflict()
        {
            Lost = new List<ClaimClash>();
            Won = new List<ClaimClash>();
        }

        public bool Refused { get { return Lost.Count > 0; } }

        /*
         * Nothing this script wanted is left to it, and it declared no way to
         * carry on without. Three states rather than two, because the UI has to
         * distinguish them: a partly refused script still does half its job, and
         * a script with a fallback -- Well Fed routing its damage through Damage
         * Budget when Fight or Flight has the lease -- does all of it.
         *
         * Getting this wrong is worse than not warning at all. "Does nothing"
         * about a script that visibly does something teaches the user that the
         * amber dot lies, and after that it protects nobody.
         */
        public bool Silenced
        {
            get
            {
                if (Wanted == 0 || Lost.Count < Wanted) return false;
                foreach (ClaimClash clash in Lost)
                    if (clash.Fallback.Length > 0) return false;
                return true;
            }
        }

        // Every lost path has somewhere else to go.
        public bool Covered
        {
            get
            {
                if (Lost.Count == 0) return false;
                foreach (ClaimClash clash in Lost)
                    if (clash.Fallback.Length == 0) return false;
                return true;
            }
        }

        // The first fallback declared, for a one-line summary.
        public string Fallback
        {
            get
            {
                foreach (ClaimClash clash in Lost)
                    if (clash.Fallback.Length > 0) return clash.Fallback;
                return "";
            }
        }

        // The script to blame, for a one-line summary. The first is the right
        // one to name: it is the highest-priority winner, so it is the one that
        // has to be moved or turned off for anything to change.
        public string Rival
        {
            get { return Lost.Count > 0 ? Lost[0].OtherName : ""; }
        }

        public string RivalFile
        {
            get { return Lost.Count > 0 ? Lost[0].OtherFile : ""; }
        }
    }

    public static class ClaimMap
    {
        /*
         * Fills in Conflict on every entry.
         *
         * Called on every rebuild rather than cached. It is O(scripts x claims)
         * over at most 64 entries with a handful of paths each, so the whole
         * calculation is smaller than the string formatting that displays it,
         * and a cache would need invalidating on six different events -- tick,
         * untick, reorder, delete, parameter change, and a script file changing
         * on disk under the watcher.
         */
        public static void Compute(IList<ScriptEntry> entries)
        {
            Dictionary<string, ScriptEntry> owner =
                new Dictionary<string, ScriptEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (ScriptEntry entry in entries) entry.Conflict = new ScriptConflict();

            foreach (ScriptEntry entry in entries)
            {
                // A disabled script claims nothing, and a script whose file is
                // gone never runs to claim anything. Counting either would
                // invent a fight that cannot happen.
                if (!entry.Enabled || entry.Missing) continue;

                foreach (ClaimDecl claim in entry.Claims)
                {
                    if (!IsActive(entry, claim)) continue;
                    entry.Conflict.Wanted++;

                    ScriptEntry holder;
                    if (!owner.TryGetValue(claim.Path, out holder))
                    {
                        owner[claim.Path] = entry;
                        continue;
                    }

                    ClaimClash lost = new ClaimClash();
                    lost.Path = claim.Path;
                    lost.OtherFile = holder.File;
                    lost.OtherName = Label(holder);
                    lost.ThisWins = false;
                    lost.Fallback = Available(entries, claim) ? claim.Fallback : "";
                    entry.Conflict.Lost.Add(lost);

                    ClaimClash won = new ClaimClash();
                    won.Path = claim.Path;
                    won.OtherFile = entry.File;
                    won.OtherName = Label(entry);
                    won.ThisWins = true;
                    won.Fallback = Available(entries, claim) ? claim.Fallback : "";
                    holder.Conflict.Won.Add(won);
                }
            }
        }

        /*
         * Whether a gated claim is actually asked for at the current settings.
         *
         * A declaration may name the neutral value itself (`off=1`, for the
         * multipliers whose no-op is 1.0 rather than 0). Failing that, both
         * default readings of "off" are honoured, because both occur in the
         * pack: a bool parameter that is false (Steady Hands' six class toggles)
         * and a number set to zero (Well Fed's damage bonus, which skips the
         * var.DamageMulAll lease entirely when it is 0).
         *
         * Everything else -- a string, a missing declaration, an unrecognised
         * type, an `off=` that does not parse as the parameter's type -- counts
         * as active. The failure to warn is the expensive direction: it is the
         * silent script the user spends a playtest on. A spurious warning is at
         * worst noise, and it names the path, so it can be read past.
         */
        private static bool IsActive(ScriptEntry entry, ClaimDecl claim)
        {
            if (string.IsNullOrEmpty(claim.When)) return true;
            object value = entry.EffectiveValue(claim.When);
            if (value == null) return true;

            if (claim.Off.Length > 0)
            {
                if (value is double)
                {
                    double off;
                    if (double.TryParse(claim.Off, NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out off))
                        return (double)value != off;
                    return true;
                }
                if (value is bool)
                {
                    bool off = claim.Off.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                               claim.Off == "1";
                    return (bool)value != off;
                }
                return !string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture),
                                      claim.Off, StringComparison.OrdinalIgnoreCase);
            }

            if (value is bool) return (bool)value;
            if (value is double) return (double)value != 0.0;
            return true;
        }

        // Whether the script a fallback depends on is actually going to run.
        // A fallback with no `needs` stands on its own.
        private static bool Available(IList<ScriptEntry> entries, ClaimDecl claim)
        {
            if (claim.Fallback.Length == 0) return false;
            if (claim.Needs.Length == 0) return true;
            foreach (ScriptEntry entry in entries)
                if (string.Equals(entry.File, claim.Needs, StringComparison.OrdinalIgnoreCase))
                    return entry.Enabled && !entry.Missing;
            return false;
        }

        private static string Label(ScriptEntry entry)
        {
            if (!string.IsNullOrEmpty(entry.Name)) return entry.Name;
            return entry.File;
        }
    }
}
