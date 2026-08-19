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

        public bool Unlisted { get; private set; }

        public ScriptEntry Entry { get { return _entry; } }
        public string File { get { return _entry.File; } }

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

        public string Secondary
        {
            get
            {
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
