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

namespace CraneManager
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

        public event PropertyChangedEventHandler PropertyChanged;

        public void RaiseAll()
        {
            Raise("Name"); Raise("Unnamed"); Raise("Enabled"); Raise("Secondary");
            Raise("StateBrush"); Raise("HasDot");
        }

        private void Raise(string property)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(property));
        }
    }
}
