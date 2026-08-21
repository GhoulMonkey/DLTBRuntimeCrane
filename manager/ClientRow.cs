// SPDX-License-Identifier: GPL-3.0-only
// One row in the Bridge-clients list.
//
// Sibling of ScriptRow and the same shape -- control, name, adaptive second
// line -- because the user is reading one column and should not have to learn
// two idioms to do it. What differs is underneath: a script row is backed by a
// manifest entry the manager owns outright, while this is backed by somebody
// else's INI that the manager is a guest in.
//
// That difference is why the checkbox is absent, rather than disabled, when a
// client declares no @enable key. A disabled control still advertises an action
// that does not exist here.

using System;
using System.ComponentModel;

namespace CraneLoader
{
    public class ClientRow : INotifyPropertyChanged
    {
        private readonly IniClient _client;
        private readonly Action<ClientRow> _onToggled;
        // Lets the window stamp its self-write guard before the file changes, so
        // its own edit does not come back as an external one.
        private readonly Action _beforeWrite;

        public ClientRow(IniClient client, Action<ClientRow> onToggled)
            : this(client, onToggled, null) { }

        public ClientRow(IniClient client, Action<ClientRow> onToggled, Action beforeWrite)
        {
            _client = client;
            _onToggled = onToggled;
            _beforeWrite = beforeWrite;
        }

        public IniClient Client { get { return _client; } }
        public string File { get { return _client.File; } }
        public bool HasEnable { get { return _client.HasEnable; } }

        // Same invariant as ScriptRow: a row always has a visible label.
        public string Name
        {
            get
            {
                if (!string.IsNullOrEmpty(_client.Name)) return _client.Name;
                if (!string.IsNullOrEmpty(_client.File)) return _client.File;
                return "(unnamed client)";
            }
        }

        public bool Enabled
        {
            get { return _client.Enabled; }
            set
            {
                if (!_client.HasEnable || _client.Enabled == value) return;
                string raw = IniFile.FromBool(value);
                if (_beforeWrite != null) _beforeWrite();
                if (!IniFile.Write(_client.Path, _client.EnableSection,
                                   _client.EnableKey, raw)) return;
                _client.Values[IniClient.Slot(_client.EnableSection,
                                              _client.EnableKey)] = raw;
                Raise("Enabled");
                Raise("Secondary");
                if (_onToggled != null) _onToggled(this);
            }
        }

        /*
         * The second line answers "what would I be changing here", which for a
         * client is its setting count -- and, when it has an off switch and is
         * off, that fact first, because it outranks everything else about it.
         *
         * A client with no @enable key never says "Off". It has no off state the
         * manager can see, and inventing one would put words in the mod's mouth.
         */
        public string Secondary
        {
            get
            {
                if (_client.HasEnable && !_client.Enabled) return "Off";
                int count = _client.Declared.Count;
                if (count == 0) return _client.Description;
                return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                                     "{0} setting{1}", count, count == 1 ? "" : "s");
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string property)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(property));
        }
    }
}
