// SPDX-License-Identifier: GPL-3.0-only
// One row in the script list, shaped for the view rather than for storage.
//
// Each row is two lines: control and name, then a secondary line that adapts to
// show the most useful thing about this row right now. A failure, if there is
// one; then an off state the user chose; then a summary of what the script is
// configured to do; and only failing all of those, its description.
//
// That is why this class exists rather than binding the ListBox straight at
// ScriptEntry: "what should this row say" is a view decision with real logic in
// it, and it does not belong in the manifest model.

using System;
using System.ComponentModel;
using System.Globalization;

namespace CraneLoader
{
    public class ScriptRow : INotifyPropertyChanged
    {
        private readonly ScriptEntry _entry;
        private readonly Action<ScriptRow> _onToggled;

        public ScriptRow(ScriptEntry entry, Action<ScriptRow> onToggled, bool unlisted)
        {
            _entry = entry;
            _onToggled = onToggled;
            Unlisted = unlisted;
        }

        // Present in \scripts but not yet recorded in the manifest.
        //
        // Kept as a flag because the code still needs it -- ticking such a row
        // has to add the entry -- but it is no longer said anywhere in the UI.
        // From the user's side a script is on or off; which file records that is
        // the manager's business.
        public bool Unlisted { get; private set; }

        public ScriptEntry Entry { get { return _entry; } }
        public string File { get { return _entry.File; } }

        /*
         * A row always has a visible label. This is the phantom-row guard.
         *
         * Symptom: the list intermittently shows a checkbox with no name beside
         * it. A row you cannot name is a row you cannot report, cannot search
         * for and cannot decide about -- and because the second line went blank
         * with it, the phantom carried no evidence about itself at all. That is
         * what made it survive two sessions.
         *
         * Two wrong guesses are already buried here. Do not add a third without
         * evidence. Ruled out, each against the real folder or the real code:
         *
         *   - Directory.GetFiles("*.lua") matching quick_hands.lua.vortex_backup
         *     through the 8.3 short-name quirk. Disproven: that file's extension
         *     is .vortex_backup and does not match. (The guard in UnlistedScripts
         *     stays anyway; the quirk is real, this just was not it.)
         *   - An entry reaching a row with an empty Name. Every path is closed:
         *     ManifestFile.Read rejects a missing or empty "file" through
         *     IsValidScriptName, LoadMetadata names a missing script after its
         *     file, and ScriptHeader.Read falls back to the filename
         *     unconditionally -- outside its try, so even an unreadable script
         *     gets a name. AddScript and OnUnlistedToggled both go through
         *     populated entries.
         *
         * Rather than guess a third time, the row now names what it is even when
         * every assumption above has failed, and says which one failed.
         */
        public string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(_entry.Name)) return _entry.Name;
                if (!string.IsNullOrEmpty(_entry.File)) return _entry.File;
                return Unlisted
                    ? "(unnamed script in \\scripts)"
                    : "(empty manifest entry)";
            }
        }

        // True when the guard above had to invent a label. Surfaced so the
        // second line can carry the diagnosis rather than staying blank.
        public bool Unnamed { get { return string.IsNullOrEmpty(_entry.Name); } }

        public bool Enabled
        {
            get { return _entry.Enabled; }
            set
            {
                if (_entry.Enabled == value) return;
                _entry.Enabled = value;
                Raise("Enabled");
                Raise("Secondary");
                Raise("StateBrush");
                Raise("HasConflict");
                Raise("ConflictTip");
                if (_onToggled != null) _onToggled(this);
            }
        }

        // The adaptive second line.
        public string Secondary
        {
            get
            {
                // Ahead of everything else, because it means the manager's own
                // invariants broke and that outranks anything about the script.
                if (Unnamed)
                    return string.IsNullOrEmpty(_entry.File)
                        ? "No filename recorded -- please report this row"
                        : "Header unread -- please report this row";

                switch (_entry.State)
                {
                    case ScriptState.Failed:
                        return "Failed to load";
                    case ScriptState.Missing:
                        return "Missing from \\scripts";
                    default:
                        break;
                }
                /*
                 * Ahead of the settings count, and ahead of the description,
                 * because those describe a script that is working and this one
                 * is not.
                 *
                 * It sits below Failed and Missing on purpose: those are things
                 * the runtime observed, this is something the manager predicted,
                 * and an observation outranks a prediction when they disagree.
                 */
                if (_entry.Enabled && _entry.Conflict.Refused)
                {
                    ScriptConflict conflict = _entry.Conflict;
                    string rival = conflict.Rival;
                    if (conflict.Silenced)
                        return "Does nothing while " + rival + " is on";
                    if (conflict.Covered)
                        return rival + " has it; " + conflict.Fallback;
                    return string.Format(CultureInfo.InvariantCulture,
                                         "{0} of {1} settings taken by {2}",
                                         conflict.Lost.Count, conflict.Wanted, rival);
                }

                if (!_entry.Enabled) return "Off";
                if (_entry.Declared.Count > 0)
                    return string.Format(CultureInfo.InvariantCulture, "{0} setting{1}",
                                         _entry.Declared.Count,
                                         _entry.Declared.Count == 1 ? "" : "s");
                if (_entry.Description.Length > 0) return _entry.Description;
                return _entry.State == ScriptState.Loaded ? "Loaded" : "";
            }
        }

        /*
         * The dot reports what the runtime said. It never restates the checkbox.
         *
         * It was amber for "enabled but unconfirmed", which sounds reasonable and
         * is wrong in practice: the game is usually not running while somebody
         * configures, so amber was the normal state and carried no information.
         *
         * Green loaded, red failed, and nothing at all when there is no news --
         * the checkbox already says whether the user wants it on.
         */
        public string StateBrush
        {
            get
            {
                switch (_entry.State)
                {
                    case ScriptState.Failed:
                    case ScriptState.Missing: return "StateBad";
                    case ScriptState.Loaded: return "StateGood";
                    default: return "StateIdle";
                }
            }
        }

        public bool HasDot
        {
            get
            {
                return _entry.State == ScriptState.Loaded ||
                       _entry.State == ScriptState.Failed ||
                       _entry.State == ScriptState.Missing;
            }
        }

        /*
         * The conflict marker: an amber dot, on the losing row only.
         *
         * Only the loser, though both sides of a clash are known. The winner is
         * doing exactly what it was asked to; marking it too would put two
         * warnings on the list for one problem and make the user check both to
         * find out which of them is the broken one.
         *
         * It is a separate dot from StateBrush rather than another value of it
         * because they answer different questions -- StateBrush is what the
         * runtime reported, this is what the manifest implies -- and a script
         * can perfectly well be reported Loaded and be refused every path it
         * asked for. That combination is precisely the failure this feature
         * exists to catch, so the two indicators must be able to show at once.
         */
        // Covered is excluded: the script declared what it does instead and it
        // still works, so an amber dot would be raising an alarm about a
        // situation its author already answered. The second line still says
        // what is going on, because two scripts reaching for one lever is worth
        // knowing even when it costs nothing.
        public bool HasConflict
        {
            get { return _entry.Enabled && _entry.Conflict.Refused && !_entry.Conflict.Covered; }
        }

        public string ConflictTip
        {
            get
            {
                if (!HasConflict) return null;
                ScriptConflict conflict = _entry.Conflict;
                string paths = "";
                foreach (ClaimClash clash in conflict.Lost)
                    paths += (paths.Length == 0 ? "" : "\n") + clash.Path;
                return string.Format(CultureInfo.InvariantCulture,
                    "{0} sits higher in the load order and holds {1}:\n{2}\n\n" +
                    "Select this script to move it above {0}, or turn one of them off.",
                    conflict.Rival, conflict.Lost.Count == 1 ? "this path" : "these paths",
                    paths);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public void RaiseAll()
        {
            Raise("Name"); Raise("Unnamed"); Raise("Enabled"); Raise("Secondary");
            Raise("StateBrush"); Raise("HasDot");
            Raise("HasConflict"); Raise("ConflictTip");
        }

        private void Raise(string property)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(property));
        }
    }
}
