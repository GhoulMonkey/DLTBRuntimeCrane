using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Media;

namespace CraneManager
{
    public partial class MainWindow : Window
    {
        private List<ScriptEntry> _entries = new List<ScriptEntry>();
        private string _folder = "";
        private FileSystemWatcher _scriptWatcher;
        private FileSystemWatcher _configWatcher;
        private FileSystemWatcher _clientWatcher;
        private System.Windows.Threading.DispatcherTimer _settle;
        private System.Windows.Threading.DispatcherTimer _heartbeat;
        private DateTime _lastSelfWrite = DateTime.MinValue;

        private bool _reportWasFresh;
        private List<IniClient> _clients = new List<IniClient>();

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _folder = ResolveDefaultFolder();
            if (_folder.Length == 0)
            {
                StatusText.Text = "DLTBRuntimeCrane.asi not found. Use Settings to point at your install.";
                return;
            }

            if (FirstRun.IsNeeded(_folder))
            {
                try { FirstRun.Create(_folder); }
                catch (Exception error)
                {
                    StatusText.Text = "Could not create Crane's settings files: " + error.Message;
                    return;
                }
            }

            ApplyScale(Settings.LoadScale());
            _reportWasFresh = GameLaunch.IsGameRunning() && StatusFile.IsFresh(_folder);
            LoadManifest();
            BuildRows();
            UpdateConnection();
            UpdateSummary();
            StartWatching();
        }

        private void StartWatching()
        {
            _settle = new System.Windows.Threading.DispatcherTimer();
            _settle.Interval = TimeSpan.FromMilliseconds(300);
            _settle.Tick += OnSettled;

            _heartbeat = new System.Windows.Threading.DispatcherTimer();
            _heartbeat.Interval = TimeSpan.FromSeconds(2);
            _heartbeat.Tick += delegate { OnHeartbeat(); };
            _heartbeat.Start();

            if (_folder.Length == 0 || !Directory.Exists(_folder)) return;
            try
            {
                Directory.CreateDirectory(Path.Combine(_folder, "scripts"));

                _scriptWatcher = MakeWatcher(Path.Combine(_folder, "scripts"), "*.lua");
                _configWatcher = MakeWatcher(_folder, "DLTBRuntimeCrane.*");

                _clientWatcher = MakeWatcher(_folder, "*.ini");
            }
            catch (Exception)
            {
                _scriptWatcher = null;
                _configWatcher = null;
                _clientWatcher = null;
            }
        }

        private FileSystemWatcher MakeWatcher(string path, string filter)
        {
            FileSystemWatcher watcher = new FileSystemWatcher(path, filter);
            watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite |
                                   NotifyFilters.Size;
            watcher.Created += OnFileEvent;
            watcher.Deleted += OnFileEvent;
            watcher.Renamed += OnFileEvent;
            watcher.Changed += OnFileEvent;
            watcher.EnableRaisingEvents = true;
            return watcher;
        }

        private void OnFileEvent(object sender, FileSystemEventArgs e)
        {
            string name = e.Name == null ? "" : e.Name;

            if (name.EndsWith(".asi", StringComparison.OrdinalIgnoreCase)) return;

            if (name.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase) &&
                (DateTime.UtcNow - _lastSelfWrite).TotalMilliseconds < 1500) return;

            if (name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) &&
                (DateTime.UtcNow - _lastSelfWrite).TotalMilliseconds < 1500) return;

            try
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    _settle.Stop();
                    _settle.Start();
                }));
            }
            catch (Exception) { }
        }

        private void OnHeartbeat()
        {
            bool fresh = GameLaunch.IsGameRunning() && StatusFile.IsFresh(_folder);
            UpdateConnection();
            if (fresh == _reportWasFresh) return;
            _reportWasFresh = fresh;
            LoadMetadata();
            BuildRows();
            UpdateSummary();
        }

        private void OnSettled(object sender, EventArgs e)
        {
            _settle.Stop();
            if (_folder.Length == 0) return;
            LoadManifest();
            BuildRows();
            UpdateConnection();
            UpdateSummary();

            ClientRow chosen = ClientList.SelectedItem as ClientRow;
            if (chosen != null && !DetailHasFocus()) ShowClientDetail(chosen);
        }

        private bool DetailHasFocus()
        {
            DependencyObject focused =
                System.Windows.Input.Keyboard.FocusedElement as DependencyObject;
            while (focused != null)
            {
                if (ReferenceEquals(focused, Detail)) return true;
                DependencyObject parent = null;
                if (focused is Visual || focused is System.Windows.Media.Media3D.Visual3D)
                    parent = System.Windows.Media.VisualTreeHelper.GetParent(focused);
                if (parent == null) parent = LogicalTreeHelper.GetParent(focused);
                focused = parent;
            }
            return false;
        }

        private void BuildRows()
        {
            List<ScriptRow> rows = new List<ScriptRow>();
            foreach (ScriptEntry entry in _entries)
            {
                if (entry.Missing) continue;
                rows.Add(new ScriptRow(entry, OnRowToggled, false));
            }

            foreach (string file in UnlistedScripts())
            {
                ScriptEntry entry = new ScriptEntry();
                entry.File = file;
                entry.Enabled = false;
                string name, description;
                List<ParamDecl> declared;
                ScriptHeader.Read(Path.Combine(_folder, "scripts", file),
                                  out name, out description, out declared);
                entry.Name = name;
                entry.Description = description;
                entry.Declared = declared;
                entry.State = ScriptState.Unknown;
                rows.Add(new ScriptRow(entry, OnUnlistedToggled, true));
            }

            string wasSelected = null;
            ScriptRow previous = ScriptList.SelectedItem as ScriptRow;
            if (previous != null) wasSelected = previous.File;

            ScriptList.ItemsSource = rows;

            if (wasSelected != null)
                foreach (ScriptRow row in rows)
                    if (string.Equals(row.File, wasSelected, StringComparison.OrdinalIgnoreCase))
                    {
                        ScriptList.SelectedItem = row;
                        break;
                    }

            BuildClientRows();
        }

        private void BuildClientRows()
        {
            List<ClientRow> rows = new List<ClientRow>();
            _clients = new List<IniClient>();
            string[] files;
            try { files = Directory.GetFiles(_folder, "*.ini"); }
            catch (IOException) { files = new string[0]; }
            catch (UnauthorizedAccessException) { files = new string[0]; }

            foreach (string path in files)
            {
                if (!string.Equals(Path.GetExtension(path), ".ini",
                                   StringComparison.OrdinalIgnoreCase)) continue;
                if (IsPlatformIni(Path.GetFileName(path))) continue;
                IniClient client = IniFile.Read(path);
                if (client == null) continue;
                _clients.Add(client);
                rows.Add(new ClientRow(client, OnClientToggled,
                    delegate { _lastSelfWrite = DateTime.UtcNow; }));
            }

            string wasSelected = null;
            ClientRow previous = ClientList.SelectedItem as ClientRow;
            if (previous != null) wasSelected = previous.File;

            ClientList.ItemsSource = rows;

            if (wasSelected != null)
                foreach (ClientRow row in rows)
                    if (string.Equals(row.File, wasSelected, StringComparison.OrdinalIgnoreCase))
                    {
                        ClientList.SelectedItem = row;
                        break;
                    }
        }

        private void OnClientToggled(ClientRow row)
        {
            StatusText.Text = row.Name + (row.Enabled ? " enabled." : " disabled.") +
                              " Takes effect when the mod next reads its INI.";
        }

        private void OnClientSelected(object sender, SelectionChangedEventArgs e)
        {
            ClientRow row = ClientList.SelectedItem as ClientRow;
            if (row == null) return;
            ScriptList.SelectedItem = null;
            ShowClientDetail(row);
        }

        private List<string> UnlistedScripts()
        {
            List<string> unlisted = new List<string>();
            string dir = Path.Combine(_folder, "scripts");
            if (!Directory.Exists(dir)) return unlisted;
            string[] files;
            try { files = Directory.GetFiles(dir, "*.lua"); }
            catch (IOException) { return unlisted; }
            foreach (string path in files)
            {
                if (!string.Equals(Path.GetExtension(path), ".lua",
                                   StringComparison.OrdinalIgnoreCase)) continue;
                string file = Path.GetFileName(path);
                bool listed = false;
                foreach (ScriptEntry entry in _entries)
                    if (string.Equals(entry.File, file, StringComparison.OrdinalIgnoreCase))
                    { listed = true; break; }
                if (!listed) unlisted.Add(file);
            }
            return unlisted;
        }

        private void OnRowToggled(ScriptRow row)
        {
            Save();
            UpdateSummary();
        }

        private void OnUnlistedToggled(ScriptRow row)
        {
            if (!row.Enabled) return;
            _entries.Add(row.Entry);
            Save();
            BuildRows();
            UpdateSummary();
        }

        private void Save()
        {
            try
            {
                _lastSelfWrite = DateTime.UtcNow;
                ManifestFile.Save(Path.Combine(_folder, "DLTBRuntimeCrane.manifest.json"), _entries);
                StatusText.Text = "Saved. Crane picks it up within a second.";
            }
            catch (Exception error)
            {
                StatusText.Text = "Could not save the manifest: " + error.Message;
            }
        }

        private static string ResolveTyped(string typed)
        {
            if (typed.Length == 0) return "";
            string[] candidates = new string[]
            {
                typed,
                Path.Combine(typed, "ph_ft", "work", "bin", "x64"),
                Path.Combine(typed, "work", "bin", "x64"),
                Path.Combine(typed, "bin", "x64"),
                Path.Combine(typed, "x64")
            };
            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(Path.Combine(candidate, "DLTBRuntimeCrane.asi")))
                        return Path.GetFullPath(candidate);
                }
                catch (ArgumentException) { }
                catch (NotSupportedException) { }
                catch (PathTooLongException) { }
            }
            return "";
        }

        private void OnAdd(object sender, RoutedEventArgs e)
        {
            if (!Ready()) return;
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Filter = "Lua scripts (*.lua)|*.lua";
            dialog.Multiselect = true;
            if (dialog.ShowDialog(this) != true) return;
            foreach (string path in dialog.FileNames) AddScript(path);
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!Ready()) return;
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null) return;
            foreach (string path in paths)
                if (path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) AddScript(path);
        }

        private void AddScript(string source)
        {
            string file = Path.GetFileName(source);
            if (!ManifestFile.IsValidScriptName(file))
            {
                StatusText.Text = "\"" + file + "\" is not a usable script name.";
                return;
            }
            foreach (ScriptEntry existing in _entries)
                if (string.Equals(existing.File, file, StringComparison.OrdinalIgnoreCase))
                {
                    StatusText.Text = file + " is already in the list.";
                    return;
                }
            try
            {
                string scripts = Path.Combine(_folder, "scripts");
                Directory.CreateDirectory(scripts);
                string target = Path.Combine(scripts, file);
                if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(target),
                                   StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(target) &&
                        MessageBox.Show(this, file + " already exists in scripts\\. Overwrite it?",
                                        "CraneManager", MessageBoxButton.YesNo,
                                        MessageBoxImage.Question) != MessageBoxResult.Yes)
                        return;
                    File.Copy(source, target, true);
                }
            }
            catch (Exception error)
            {
                StatusText.Text = "Could not copy the script: " + error.Message;
                return;
            }

            ScriptEntry entry = new ScriptEntry();
            entry.File = file;
            entry.Enabled = false;
            _entries.Add(entry);
            LoadMetadata();
            Save();
            BuildRows();
            UpdateSummary();
            StatusText.Text = file + " added, disabled. Tick it to run it.";
        }

        private void OnReload(object sender, RoutedEventArgs e)
        {
            if (!Ready()) return;
            LoadManifest();
            BuildRows();
            UpdateConnection();
            UpdateSummary();
            StatusText.Text = "Re-read from disk.";
        }

        private void OnLaunch(object sender, RoutedEventArgs e)
        {
            if (!Ready()) return;
            string exe = Path.Combine(_folder, GameLaunch.Executable);
            if (!File.Exists(exe))
            {
                StatusText.Text = GameLaunch.Executable + " is not in this folder, so there is nothing to launch.";
                return;
            }

            GameLaunch.Route route =
                GameLaunch.Plan(_folder, GameLaunch.IsSteamClientRunning());
            try
            {
                if (route == GameLaunch.Route.SteamUri)
                {
                    System.Diagnostics.Process.Start(GameLaunch.SteamUri);
                }
                else
                {
                    System.Diagnostics.ProcessStartInfo start =
                        new System.Diagnostics.ProcessStartInfo(exe);

                    start.WorkingDirectory = _folder;
                    if (route == GameLaunch.Route.DirectWithSteamEnv)
                    {
                        start.UseShellExecute = false;
                        GameLaunch.ApplySteamEnvironment(start.EnvironmentVariables);
                    }
                    else
                    {
                        start.UseShellExecute = true;
                    }
                    System.Diagnostics.Process.Start(start);
                }
            }
            catch (Exception error)
            {
                StatusText.Text = "Could not start the game: " + error.Message;
                return;
            }
            int enabled = 0;
            foreach (ScriptEntry entry in _entries) if (entry.Enabled) enabled++;

            StatusText.Text = string.Format(CultureInfo.InvariantCulture,
                route == GameLaunch.Route.SteamUri
                    ? "Asked Steam to start, with {0} script(s) enabled. Steam has to start first."
                    : "Launching with {0} script(s) enabled.", enabled);
        }

        private bool Ready()
        {
            if (_folder.Length > 0) return true;
            StatusText.Text = "No Crane install selected. Use Settings to point at your game folder.";
            return false;
        }

        private void MoveSelected(int delta)
        {
            ScriptRow row = ScriptList.SelectedItem as ScriptRow;
            if (row == null || row.Unlisted) return;
            int from = _entries.IndexOf(row.Entry);
            if (from < 0) return;
            int to = from + delta;
            if (to < 0 || to >= _entries.Count) return;

            ScriptEntry moved = _entries[from];
            _entries.RemoveAt(from);
            _entries.Insert(to, moved);
            Save();
            BuildRows();

            foreach (object item in (System.Collections.IEnumerable)ScriptList.ItemsSource)
            {
                ScriptRow candidate = item as ScriptRow;
                if (candidate != null && ReferenceEquals(candidate.Entry, moved))
                {
                    ScriptList.SelectedItem = candidate;
                    break;
                }
            }
            UpdateSummary();
        }

        private void RemoveSelected(ScriptRow row)
        {
            if (row == null) return;
            if (MessageBox.Show(this,
                    "Delete " + row.File + " from \\scripts?\r\n\r\n" +
                    "The file is removed from disk. Its settings are forgotten.",
                    "CraneManager", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            try
            {
                File.Delete(Path.Combine(_folder, "scripts", row.File));
            }
            catch (Exception error)
            {
                StatusText.Text = "Could not delete " + row.File + ": " + error.Message;
                return;
            }
            _entries.Remove(row.Entry);
            Save();
            LoadMetadata();
            BuildRows();
            UpdateSummary();
            StatusText.Text = row.File + " deleted.";
        }

        private void ApplyScale(double scale)
        {
            RootScale.ScaleX = scale;
            RootScale.ScaleY = scale;
        }

        private void OnSettings(object sender, RoutedEventArgs e)
        {
            Window dialog = new Window();
            dialog.Title = "Settings";
            dialog.Owner = this;

            dialog.ResizeMode = ResizeMode.CanResize;
            dialog.Width = 560;
            dialog.Height = 640;
            dialog.MinWidth = 380;
            dialog.MinHeight = 260;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(16);
            panel.MinWidth = 460;

            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.Content = panel;

            double zoom = Settings.LoadScale();
            scroller.LayoutTransform = new ScaleTransform(zoom, zoom);

            TextBlock folderLabel = new TextBlock();
            folderLabel.Text = "Game folder";
            folderLabel.Style = (Style)FindResource("SectionHeading");
            folderLabel.Margin = new Thickness(0, 0, 0, 4);
            panel.Children.Add(folderLabel);

            TextBox folderBox = new TextBox();
            folderBox.Text = _folder;
            TextBlock folderHint = new TextBlock();
            folderHint.Text = "Where Dying Light: The Beast is installed. " +
                              "The game's own folder is fine - Crane is found underneath it.";
            folderHint.Style = (Style)FindResource("Subtle");
            folderHint.FontSize = 11;
            folderHint.Margin = new Thickness(0, 4, 0, 0);

            panel.Children.Add(folderBox);
            panel.Children.Add(folderHint);

            TextBlock scaleLabel = new TextBlock();
            scaleLabel.Text = "UI scale";
            scaleLabel.Style = (Style)FindResource("SectionHeading");
            panel.Children.Add(scaleLabel);

            ComboBox scale = new ComboBox();
            int[] steps = new int[] { 75, 90, 100, 110, 125, 150, 175, 200 };
            double current = Settings.LoadScale();
            foreach (int step in steps)
            {
                ComboBoxItem item = new ComboBoxItem();
                item.Content = step + "%";
                item.Tag = step;
                scale.Items.Add(item);
                if (Math.Abs(step / 100.0 - current) < 0.01) scale.SelectedItem = item;
            }
            scale.SelectionChanged += delegate
            {
                ComboBoxItem item = scale.SelectedItem as ComboBoxItem;
                if (item == null) return;
                double chosen = (int)item.Tag / 100.0;
                ApplyScale(chosen);
                Settings.SaveScale(chosen);

                scroller.LayoutTransform = new ScaleTransform(chosen, chosen);
            };
            panel.Children.Add(scale);

            foreach (string name in PlatformInis)
            {
                IniClient platform = ReadPlatformIni(name);
                if (platform == null) continue;

                StackPanel inner = new StackPanel();
                if (platform.Description.Length > 0)
                {
                    TextBlock about = new TextBlock();
                    about.Text = platform.Description;
                    about.Style = (Style)FindResource("Subtle");
                    about.FontSize = 11;
                    about.TextWrapping = TextWrapping.Wrap;
                    about.Margin = new Thickness(0, 0, 0, 4);
                    inner.Children.Add(about);
                }
                AppendSettings(platform, inner);

                inner.Margin = new Thickness(14, 0, 0, 0);

                TextBlock header = new TextBlock();
                header.Text = platform.Name;
                header.FontWeight = FontWeights.SemiBold;
                header.FontSize = 13;
                header.Foreground = (Brush)FindResource("Ink");
                header.Margin = new Thickness(0, 18, 0, 4);

                Border rule = new Border();
                rule.Height = 1;
                rule.Background = (Brush)FindResource("Rule");
                rule.Margin = new Thickness(0, 0, 0, 6);

                panel.Children.Add(header);
                panel.Children.Add(rule);
                panel.Children.Add(inner);
            }

            Button close = new Button();
            close.Content = "Close";
            close.Padding = new Thickness(16, 4, 16, 4);
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 16, 0, 0);
            close.Click += delegate
            {
                string typed = ResolveTyped(folderBox.Text.Trim().Trim('"'));
                if (typed.Length > 0 && typed != _folder)
                {
                    _folder = typed;
                    Settings.SaveFolder(typed);
                    LoadManifest();
                    BuildRows();
                    UpdateConnection();
                    UpdateSummary();
                }
                dialog.Close();
            };
            panel.Children.Add(close);

            dialog.Content = scroller;
            dialog.ShowDialog();
        }

        private void OnScriptSelected(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            ScriptRow chosen = ScriptList.SelectedItem as ScriptRow;
            if (chosen != null) ClientList.SelectedItem = null;
            ShowDetail(chosen);
        }

        private static string ResolveDefaultFolder()
        {
            string here = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(here))
            {
                string[] relative = new string[]
                {
                    here,
                    Path.Combine(here, "work", "bin", "x64"),
                    Path.Combine(here, "ph_ft", "work", "bin", "x64")
                };
                foreach (string candidate in relative)
                {
                    try
                    {
                        if (File.Exists(Path.Combine(candidate, "DLTBRuntimeCrane.asi")))
                            return Path.GetFullPath(candidate);
                    }
                    catch (ArgumentException) { }
                    catch (NotSupportedException) { }
                    catch (PathTooLongException) { }
                }
            }
            string saved = Settings.LoadFolder();
            if (saved.Length > 0 && File.Exists(Path.Combine(saved, "DLTBRuntimeCrane.asi")))
                return saved;
            return "";
        }

        private void LoadManifest()
        {
            string path = Path.Combine(_folder, "DLTBRuntimeCrane.manifest.json");
            _entries = new List<ScriptEntry>();
            if (!File.Exists(path)) return;
            try
            {
                _entries = ManifestFile.Read(File.ReadAllText(path));
            }
            catch (ManifestException error)
            {
                StatusText.Text = "Manifest could not be read -- " + error.Message;
                return;
            }

            LoadMetadata();
        }

        private void LoadMetadata()
        {
            StatusReport report = GameLaunch.IsGameRunning() ? StatusFile.Read(_folder) : null;
            foreach (ScriptEntry entry in _entries)
            {
                string script = Path.Combine(_folder, "scripts", entry.File);
                entry.Missing = !File.Exists(script);
                string name, description;
                List<ParamDecl> declared;
                if (entry.Missing)
                {
                    name = entry.File;
                    description = "Not present in \\scripts";
                    declared = new List<ParamDecl>();
                }
                else
                {
                    ScriptHeader.Read(script, out name, out description, out declared);
                }
                entry.Name = name;
                entry.Description = description;
                entry.Declared = declared;
                entry.State = report == null ? ScriptState.Unknown : report.StateOf(entry.File);
                entry.StateError = report == null ? "" : report.ErrorOf(entry.File);
            }
        }

        private void UpdateConnection()
        {
            string status = Path.Combine(_folder, StatusFile.FileName);

            bool gameUp = GameLaunch.IsGameRunning();
            bool reporting = gameUp && StatusFile.IsFresh(_folder);
            ConnectionText.Text = reporting ? "Game: connected"
                                : gameUp ? "Game: starting..."
                                : "Game: not running";
            bool running = reporting;
            ConnectionText.ToolTip = File.Exists(status)
                ? "Last report from Crane: " + File.GetLastWriteTime(status).ToString("HH:mm:ss")
                : "Crane has not written a status report in this folder yet.";
            ConnectionDot.Fill = running
                ? (Brush)FindResource("StateGood")
                : (Brush)FindResource("StateIdle");
        }

        private void ShowDetail(ScriptRow row)
        {
            Detail.Children.Clear();
            if (row == null)
            {
                Detail.Children.Add(Muted("Select a script to see what it does and what you can change."));
                return;
            }

            ScriptEntry entry = row.Entry;

            TextBlock title = new TextBlock();
            title.Text = entry.Name;
            title.Style = (Style)FindResource("Title");
            Detail.Children.Add(title);

            TextBlock file = new TextBlock();
            file.Text = entry.File;
            file.Foreground = (Brush)FindResource("InkFaint");
            file.Margin = new Thickness(0, 1, 0, 0);
            Detail.Children.Add(file);

            if (entry.State == ScriptState.Failed && entry.StateError.Length > 0)
            {
                Border alert = new Border();
                alert.Background = new SolidColorBrush(Color.FromRgb(0xFD, 0xF2, 0xF1));
                alert.BorderBrush = (Brush)FindResource("StateBad");
                alert.BorderThickness = new Thickness(0, 0, 0, 0);
                alert.Padding = new Thickness(10, 8, 10, 8);
                alert.Margin = new Thickness(0, 12, 0, 0);
                TextBlock text = new TextBlock();
                text.Text = entry.StateError;
                text.TextWrapping = TextWrapping.Wrap;
                text.Foreground = (Brush)FindResource("StateBad");
                alert.Child = text;
                Detail.Children.Add(alert);
            }

            if (entry.Description.Length > 0)
            {
                TextBlock description = new TextBlock();
                description.Text = entry.Description;
                description.Style = (Style)FindResource("Subtle");
                description.Margin = new Thickness(0, 12, 0, 0);
                Detail.Children.Add(description);
            }

            if (entry.Declared.Count == 0)
            {
                Detail.Children.Add(Muted("This script has no settings. A script author adds them with @param lines in the file header."));
                return;
            }

            List<string> groups = new List<string>();
            foreach (ParamDecl decl in entry.Declared)
                if (decl.Group.Length == 0) { groups.Add(""); break; }
            foreach (ParamDecl decl in entry.Declared)
                if (decl.Group.Length > 0 && !groups.Contains(decl.Group))
                    groups.Add(decl.Group);

            foreach (string group in groups)
            {
                TextBlock heading = new TextBlock();
                heading.Text = group.Length == 0 ? "Settings" : group;
                heading.Style = (Style)FindResource("SectionHeading");
                Detail.Children.Add(heading);

                foreach (ParamDecl decl in entry.Declared)
                    if (decl.Group == group)
                        Detail.Children.Add(SettingRow(entry, decl));
            }

            Button remove = new Button();
            remove.Content = "Delete script";
            remove.Padding = new Thickness(12, 4, 12, 4);
            remove.HorizontalAlignment = HorizontalAlignment.Right;
            remove.Margin = new Thickness(0, 12, 0, 0);
            remove.Click += delegate { RemoveSelected(ScriptList.SelectedItem as ScriptRow); };

            Button reset = new Button();
            reset.Content = "Reset defaults";
            reset.Padding = new Thickness(12, 4, 12, 4);
            reset.HorizontalAlignment = HorizontalAlignment.Right;
            reset.Margin = new Thickness(0, 12, 0, 0);
            reset.Click += delegate
            {
                foreach (ParamDecl decl in entry.Declared)
                    entry.Params[decl.Key] = decl.Default;
                Save();
                ShowDetail(ScriptList.SelectedItem as ScriptRow);
            };

            Button up = new Button();
            up.Content = "Move up";
            up.Padding = new Thickness(12, 4, 12, 4);
            up.IsEnabled = _entries.IndexOf(entry) > 0;
            up.Click += delegate { MoveSelected(-1); };

            Button down = new Button();
            down.Content = "Move down";
            down.Padding = new Thickness(12, 4, 12, 4);
            down.Margin = new Thickness(8, 0, 0, 0);
            int at = _entries.IndexOf(entry);
            down.IsEnabled = at >= 0 && at < _entries.Count - 1;
            down.Click += delegate { MoveSelected(1); };

            StackPanel order = new StackPanel();
            order.Orientation = Orientation.Horizontal;
            order.HorizontalAlignment = HorizontalAlignment.Left;
            order.Margin = new Thickness(0, 16, 0, 0);
            order.Children.Add(up);
            order.Children.Add(down);
            Detail.Children.Add(order);

            Detail.Children.Add(Muted(
                "Order decides who wins. If two enabled scripts claim the same " +
                "thing, the one higher in this list gets it and the other is refused."));

            StackPanel actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            reset.Margin = new Thickness(8, 12, 0, 0);
            actions.Children.Add(remove);
            actions.Children.Add(reset);
            Detail.Children.Add(actions);
        }

        private void ShowClientDetail(ClientRow row)
        {
            Detail.Children.Clear();
            if (row == null) return;
            IniClient client = row.Client;

            TextBlock title = new TextBlock();
            title.Text = row.Name;
            title.Style = (Style)FindResource("Title");
            Detail.Children.Add(title);

            TextBlock file = new TextBlock();
            file.Text = client.File;
            file.Foreground = (Brush)FindResource("InkFaint");
            file.Margin = new Thickness(0, 1, 0, 0);
            Detail.Children.Add(file);

            if (client.Description.Length > 0)
            {
                TextBlock description = new TextBlock();
                description.Text = client.Description;
                description.Style = (Style)FindResource("Subtle");
                description.TextWrapping = TextWrapping.Wrap;
                description.Margin = new Thickness(0, 12, 0, 0);
                Detail.Children.Add(description);
            }

            if (client.Declared.Count == 0)
            {
                Detail.Children.Add(Muted("This client declares no settings. A mod author adds them with @param comments in its INI."));
                return;
            }

            if (client.HasEnable &&
                IniFile.Find(client, client.EnableSection, client.EnableKey) == null)
            {
                ParamDecl decl = new ParamDecl();
                decl.Key = client.EnableKey;
                decl.Type = ParamType.Bool;
                decl.Label = "Enabled";
                decl.Default = true;
                decl.Desc = "Turns this mod on or off in its own INI. Some clients " +
                            "only read this at startup.";
                client.Declared.Insert(0, new IniSetting(decl, client.EnableSection));
            }

            AppendSettings(client, Detail);

            Detail.Children.Add(Muted("Changes are written straight to the INI. When they take effect is up to the mod -- some re-read while the game runs, some only at startup."));
        }

        private void AppendSettings(IniClient client, Panel target)
        {
            List<string> groups = new List<string>();
            foreach (IniSetting setting in client.Declared)
                if (setting.Decl.Group.Length == 0) { groups.Add(""); break; }
            foreach (IniSetting setting in client.Declared)
                if (setting.Decl.Group.Length > 0 && !groups.Contains(setting.Decl.Group))
                    groups.Add(setting.Decl.Group);

            foreach (string group in groups)
            {
                TextBlock heading = new TextBlock();
                heading.Text = group.Length == 0 ? "Settings" : group;
                heading.Style = (Style)FindResource("SectionHeading");
                target.Children.Add(heading);

                foreach (IniSetting setting in client.Declared)
                    if (setting.Decl.Group == group)
                        target.Children.Add(ClientSettingRow(client, setting));
            }
        }

        private static readonly string[] PlatformInis = new string[]
        {
            "DLTBRuntimeCrane.ini",
            "DLTBRuntimeBridge.ini",
        };

        private IniClient ReadPlatformIni(string name)
        {
            string path = Path.Combine(_folder, name);
            bool self = string.Equals(name, "DLTBRuntimeCrane.ini",
                                      StringComparison.OrdinalIgnoreCase);
            IniClient platform = IniFile.Read(path, !self);
            if (platform == null) return null;
            if (!self) return platform;

            if (platform.Name.Length == 0 || platform.Name == "DLTBRuntimeCrane")
                platform.Name = "CRANE";
            if (platform.Description.Length == 0)
                platform.Description = "The Lua scripting host. These are its own settings.";

            if (IniFile.Find(platform, "Crane", "AllowWrites") == null &&
                platform.Has("Crane", "AllowWrites"))
            {
                ParamDecl writes = new ParamDecl();
                writes.Key = "AllowWrites";
                writes.Type = ParamType.Bool;
                writes.Label = "Allow scripts to change the game";
                writes.Group = "Safety";
                writes.Default = false;
                writes.Desc = "Off, scripts may only read. On, enabled scripts can " +
                              "write game state, hold leases and change a save you " +
                              "care about. Takes effect without restarting; turning " +
                              "it off releases everything scripts are holding.";
                platform.Declared.Add(new IniSetting(writes, "Crane"));
            }
            return platform;
        }

        private static bool IsPlatformIni(string fileName)
        {
            foreach (string known in PlatformInis)
                if (string.Equals(fileName, known, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private UIElement ClientSettingRow(IniClient client, IniSetting setting)
        {
            ParamDecl decl = setting.Decl;
            Grid grid = new Grid();
            grid.Margin = new Thickness(0, 0, 0, 10);
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());

            TextBlock label = new TextBlock();
            label.Text = decl.Label;
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            bool declaredButAbsent = !client.Has(setting.Section, decl.Key);

            object current = IniFile.ToValue(decl, client.Raw(setting.Section, decl.Key));
            FrameworkElement editor = MakeClientEditor(client, setting, current);
            editor.HorizontalAlignment = HorizontalAlignment.Right;
            editor.Margin = new Thickness(12, 0, 0, 0);

            if (declaredButAbsent) editor.IsEnabled = false;
            Grid.SetColumn(editor, 1);
            grid.Children.Add(editor);

            string range = decl.Type == ParamType.Number && (decl.HasMin || decl.HasMax)
                ? string.Format(CultureInfo.InvariantCulture, "{0} to {1}", decl.Min, decl.Max)
                : "";
            string hint = decl.Desc;
            if (range.Length > 0)
                hint = hint.Length > 0 ? hint + "  (" + range + ")" : range;
            if (declaredButAbsent)
                hint = "Declared in the INI's comments but not present as a key in [" +
                       setting.Section + "], so there is nothing to change.";

            if (hint.Length > 0)
            {
                TextBlock note = new TextBlock();
                note.Text = hint;
                note.FontSize = 11;
                note.Foreground = (Brush)FindResource(
                    declaredButAbsent ? "StateBad" : "InkFaint");
                note.TextWrapping = TextWrapping.Wrap;
                note.Margin = new Thickness(0, 2, 0, 0);
                Grid.SetRow(note, 1);
                Grid.SetColumn(note, 0);
                Grid.SetColumnSpan(note, 2);
                grid.Children.Add(note);
            }
            return grid;
        }

        private FrameworkElement MakeClientEditor(IniClient client, IniSetting setting, object current)
        {
            ParamDecl decl = setting.Decl;
            if (decl.Type == ParamType.Bool)
            {
                CheckBox box = new CheckBox();
                box.IsChecked = current is bool && (bool)current;
                box.VerticalAlignment = VerticalAlignment.Center;
                box.Checked += delegate { CommitClient(client, setting, true); };
                box.Unchecked += delegate { CommitClient(client, setting, false); };
                return box;
            }
            if (decl.Type == ParamType.Enum)
            {
                ComboBox combo = new ComboBox();
                combo.MinWidth = 120;
                foreach (string option in decl.Values) combo.Items.Add(option);
                combo.SelectedItem = Convert.ToString(current, CultureInfo.InvariantCulture);
                combo.SelectionChanged += delegate
                {
                    if (combo.SelectedItem != null) CommitClient(client, setting, combo.SelectedItem);
                };
                return combo;
            }

            TextBox text = new TextBox();
            text.MinWidth = 90;
            text.TextAlignment = TextAlignment.Right;
            text.Text = IniFile.ToRaw(decl, current);

            text.LostFocus += delegate
            {
                if (decl.Type != ParamType.Number) { CommitClient(client, setting, text.Text); return; }
                double number;
                if (!double.TryParse(text.Text.Trim(), NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out number))
                {
                    text.Text = IniFile.ToRaw(decl, IniFile.ToValue(decl, client.Raw(setting.Section, decl.Key)));
                    StatusText.Text = decl.Label + " must be a number; put back what it was.";
                    return;
                }

                if (decl.HasMin && number < decl.Min) number = decl.Min;
                if (decl.HasMax && number > decl.Max) number = decl.Max;
                text.Text = IniFile.ToRaw(decl, number);
                CommitClient(client, setting, number);
            };
            return text;
        }

        private void CommitClient(IniClient client, IniSetting setting, object value)
        {
            ParamDecl decl = setting.Decl;
            string raw = IniFile.ToRaw(decl, value);
            if (string.Equals(raw, client.Raw(setting.Section, decl.Key), StringComparison.Ordinal))
                return;
            _lastSelfWrite = DateTime.UtcNow;
            if (!IniFile.Write(client.Path, setting.Section, decl.Key, raw))
            {
                StatusText.Text = "Could not write " + decl.Key + " to [" + setting.Section +
                                  "] in " + client.File + ".";
                return;
            }
            client.Values[IniClient.Slot(setting.Section, decl.Key)] = raw;
            StatusText.Text = client.Name + ": " + decl.Label + " set to " + raw + ".";
        }

        private UIElement SettingRow(ScriptEntry entry, ParamDecl decl)
        {
            Grid grid = new Grid();
            grid.Margin = new Thickness(0, 0, 0, 10);
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());

            TextBlock label = new TextBlock();
            label.Text = decl.Label;
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 0);
            grid.Children.Add(label);

            object current;
            if (!entry.Params.TryGetValue(decl.Key, out current)) current = decl.Default;

            FrameworkElement editor = MakeEditor(entry, decl, current);
            editor.HorizontalAlignment = HorizontalAlignment.Right;
            editor.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(editor, 1);
            grid.Children.Add(editor);

            string range = decl.Type == ParamType.Number && (decl.HasMin || decl.HasMax)
                ? string.Format(CultureInfo.InvariantCulture, "{0} to {1}", decl.Min, decl.Max)
                : "";

            string hint = decl.Desc;
            if (range.Length > 0)
                hint = hint.Length > 0 ? hint + "  (" + range + ")" : range;

            if (hint.Length > 0)
            {
                TextBlock note = new TextBlock();
                note.Text = hint;
                note.FontSize = 11;
                note.Foreground = (Brush)FindResource("InkFaint");
                note.TextWrapping = TextWrapping.Wrap;
                note.Margin = new Thickness(0, 2, 0, 0);
                Grid.SetRow(note, 1);
                Grid.SetColumn(note, 0);
                Grid.SetColumnSpan(note, 2);
                grid.Children.Add(note);
            }
            return grid;
        }

        private FrameworkElement MakeEditor(ScriptEntry entry, ParamDecl decl, object current)
        {
            if (decl.Type == ParamType.Bool)
            {
                CheckBox box = new CheckBox();
                box.IsChecked = current is bool && (bool)current;
                box.VerticalAlignment = VerticalAlignment.Center;
                box.Checked += delegate { Commit(entry, decl.Key, true); };
                box.Unchecked += delegate { Commit(entry, decl.Key, false); };
                return box;
            }
            if (decl.Type == ParamType.Enum)
            {
                ComboBox combo = new ComboBox();
                combo.MinWidth = 140;
                foreach (string option in decl.Values) combo.Items.Add(option);
                combo.SelectedItem = Convert.ToString(current, CultureInfo.InvariantCulture);
                combo.SelectionChanged += delegate
                {
                    if (combo.SelectedItem != null)
                        Commit(entry, decl.Key, combo.SelectedItem.ToString());
                };
                return combo;
            }

            TextBox text = new TextBox();
            text.MinWidth = 90;
            text.TextAlignment = TextAlignment.Right;

            text.Text = decl.Type == ParamType.Number
                ? FormatNumber(Convert.ToDouble(current, CultureInfo.InvariantCulture), decl)
                : Convert.ToString(current, CultureInfo.InvariantCulture);
            text.LostFocus += delegate
            {
                if (decl.Type == ParamType.Number)
                {
                    double parsed;
                    if (double.TryParse(text.Text, NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out parsed))
                    {
                        if (decl.HasMin && parsed < decl.Min) parsed = decl.Min;
                        if (decl.HasMax && parsed > decl.Max) parsed = decl.Max;
                        text.Text = FormatNumber(parsed, decl);
                        Commit(entry, decl.Key, parsed);
                    }
                    else text.Text = FormatNumber(
                        Convert.ToDouble(decl.Default, CultureInfo.InvariantCulture), decl);
                }
                else Commit(entry, decl.Key, text.Text);
            };
            return text;
        }

        private static string FormatNumber(double value, ParamDecl decl)
        {
            bool wholeRange = (!decl.HasMin || decl.Min == Math.Floor(decl.Min)) &&
                              (!decl.HasMax || decl.Max == Math.Floor(decl.Max));
            if (wholeRange && value == Math.Floor(value))
                return ((long)value).ToString(CultureInfo.InvariantCulture);
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void Commit(ScriptEntry entry, string key, object value)
        {
            entry.Params[key] = value;
            Save();
        }

        private TextBlock Muted(string message)
        {
            TextBlock text = new TextBlock();
            text.Text = message;
            text.Style = (Style)FindResource("Subtle");
            text.Margin = new Thickness(0, 12, 0, 0);
            return text;
        }

        private void UpdateSummary()
        {
            int total = 0, enabled = 0, failed = 0, reported = 0;
            System.Collections.IEnumerable rows = ScriptList.ItemsSource as System.Collections.IEnumerable;
            if (rows != null)
                foreach (object item in rows)
                {
                    ScriptRow row = item as ScriptRow;
                    if (row == null) continue;
                    total++;
                    if (row.Enabled) enabled++;
                    if (row.Entry.State == ScriptState.Failed ||
                        row.Entry.State == ScriptState.Missing) failed++;
                    if (row.Entry.State != ScriptState.Unknown) reported++;
                }

            string health;
            if (failed > 0) health = failed + " failed to load";
            else if (enabled == 0) health = "nothing enabled";
            else if (reported == 0) health = "waiting for the game";
            else health = "all enabled scripts loaded";

            StatusText.Text = string.Format(CultureInfo.InvariantCulture,
                                            "{0} script{1} · {2} enabled · {3}",
                                            total, total == 1 ? "" : "s", enabled, health);
        }
    }
}
