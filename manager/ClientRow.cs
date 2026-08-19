using System;
using System.ComponentModel;

namespace CraneManager
{
    public class ClientRow : INotifyPropertyChanged
    {
        private readonly IniClient _client;
        private readonly Action<ClientRow> _onToggled;

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
