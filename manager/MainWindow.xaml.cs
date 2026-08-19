// CraneManager -- main window.
//
// Port of MainForm.cs from WinForms to WPF. The logic layer is unchanged:
// ManifestFile, ScriptHeader, StatusFile and FirstRun are reused verbatim, and
// only the presentation is rewritten.
//
// This first pass establishes the shell -- four bands, one split -- and enough
// behaviour to prove the pipeline end to end. The list rows and the settings
// pane follow.

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
        // Last known freshness of Crane's report, so the heartbeat can act on
        // the change rather than rebuilding the list twice a second.
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

        /*
         * Watches the folder, and separately ticks a clock.
         *
         * The WinForms window did this and the WPF port dropped it, which is how
         * it came to report "Game: not running" with the game plainly running:
         * everything was read once at startup and never again, so the window
         * showed the world as it had been when it opened.
         *
         * The watcher covers changes on disk -- a script added, the manifest
         * rewritten, a fresh status report. The heartbeat covers time passing,
         * which no file event announces: "connected" is inferred from the status
         * file's age, so it can stop being true without anything happening on
         * disk.
         */
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
                // Two narrow watchers rather than one recursive one: Crane is
                // installed in the game's own binary directory, which the game
                // writes into constantly, and watching that recursively is a
                // stream of events to discard.
                _scriptWatcher = MakeWatcher(Path.Combine(_folder, "scripts"), "*.lua");
                _configWatcher = MakeWatcher(_folder, "DLTBRuntimeCrane.*");
                /*
                 * The clients' own INIs, because several mods write to them
                 * while you play.
                 *
                 * AutoSurvival's marker toggle says so in its own comments:
                 * "Shift+Q or Select+RS changes this value immediately and
                 * persists it here". Auto-Eat's level chord does the same. Any
                 * of those left this window showing a value the file no longer
                 * held, and the next edit here would have written the stale one
                 * back over the user's in-game choice.
                 */
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
            // "DLTBRuntimeCrane.*" also matches the ASI, which changes only on a
            // reinstall and is no reason to rebuild the list.
            if (name.EndsWith(".asi", StringComparison.OrdinalIgnoreCase)) return;
            // Our own manifest writes come back as events. Ignoring a short
            // window after one is cheaper than hashing the file to recognise it.
            if (name.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase) &&
                (DateTime.UtcNow - _lastSelfWrite).TotalMilliseconds < 1500) return;
            // Same for a client INI this window just wrote. Without this, every
            // value committed here would bounce straight back as a refresh.
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

        /*
         * The clock's job is to notice the game going away.
         *
         * A game that exits writes nothing, so no watcher fires and nothing
         * would ever retract what the last report said. Rebuilding on every tick
         * would fight the user -- it rebuilds the list twice a second while they
         * are trying to click it -- so the rows are refreshed only on the edge,
         * when the report crosses from current to stale or back.
         */
        private void OnHeartbeat()
        {
            // The same conjunction LoadMetadata uses. Keying the edge on file
            // age alone would leave the dots lit for five minutes after the game
            // exited, which is the bug this pass is fixing one layer up.
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

            /*
             * Re-show the selected client so a value changed outside this window
             * appears here, rather than the pane keeping whatever it read when it
             * was opened.
             *
             * Not while the user is editing. Rebuilding the pane replaces every
             * control, which would take the caret out of a half-typed number and
             * discard it -- and the commit happens on lost focus, so the value
             * would vanish rather than be saved. If focus is inside the pane the
             * refresh is skipped; the next change, or reselecting the row, picks
             * it up.
             */
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

        /*
         * Rebuilds the row list, including scripts present in scripts\ that the
         * manifest does not list.
         *
         * Unlisted scripts are shown but not written to the manifest until the
         * user ticks one. Adding them on sight would fight Remove -- take a
         * script out of the list and it would reappear on the next refresh -- so
         * "present in the folder" and "listed" stay separate facts.
         */
        private void BuildRows()
        {
            List<ScriptRow> rows = new List<ScriptRow>();
            foreach (ScriptEntry entry in _entries)
            {
                /*
                 * A script whose file is no longer in \scripts drops out of the
                 * list rather than sitting there saying "Missing".
                 *
                 * The manifest entry is kept, and that distinction matters.
                 * Scripts arrive by Vortex -- quick_hands.lua is deployed
                 * from its own package -- so a purge or a redeploy makes the file
                 * vanish through no decision of the user's. Pruning the entry
                 * would take their tuned parameters with it, and they would come
                 * back to a script reset to defaults with nothing to explain why.
                 * Hiding the row costs nothing and the row returns, values intact,
                 * the moment the file does.
                 *
                 * Deleting a script through the button does still remove the
                 * entry, because that is something the user asked for.
                 */
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

            // Keep the selection across a rebuild. Ticking a script rebuilds the
            // list, and losing the detail pane at that moment is disorienting --
            // the user was looking at that script when they acted on it.
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

        /*
         * Every annotated INI beside the ASI becomes a client row.
         *
         * Discovery is by annotation: any .ini here that declares something. A
         * hardcoded list of known mods would defeat the point of putting the
         * declarations in the INI, which is that a mod this manager has never
         * heard of can opt in by shipping annotations, with no change here. An
         * unannotated INI is skipped entirely rather than listed as
         * unmanageable -- a row you cannot act on is clutter, and every game
         * folder has INIs that are none of our business.
         *
         * Crane's own INI is excluded: it is the host rather than a client, and
         * its settings live in the Settings window.
         */
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

        /*
         * The two lists share one detail pane, so selecting in one clears the
         * other. Without that the pane shows a client while a script still looks
         * selected, and the highlight points at something you are not editing.
         */
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
                /*
                 * Belt and braces on the extension.
                 *
                 * Directory.GetFiles with a three-character extension pattern
                 * also returns files whose extension merely BEGINS with it -- a
                 * documented 8.3 leftover, the one where "*.xls" returns
                 * book.xlsx. So "*.lua" would also return a hypothetical
                 * something.luaX.
                 *
                 * It does NOT catch quick_hands.lua.vortex_backup, which Vortex
                 * leaves in this folder: that file's extension is
                 * .vortex_backup, and I initially and wrongly blamed it for
                 * phantom rows. Checked against the real folder rather than
                 * assumed. The guard stays because the quirk is real even though
                 * this was not an instance of it.
                 */
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

        // Ticking an unlisted script is how it joins the list. The file turning
        // up in the folder is not enough on its own.
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

        /*
         * Accepts the game folder, not just the folder Crane happens to live in.
         *
         * The label says "Game folder", so it has to be true: Crane installs to
         * ph_ft\work\bin\x64, several levels below anything a user would call
         * their game folder. Asking for a path and then rejecting the obvious
         * answer is no use to anyone, so the likely shapes are tried in turn.
         *
         * Returns "" when none of them holds a Crane install, which leaves the
         * current folder untouched rather than pointing the window at nothing.
         */
        private static string ResolveTyped(string typed)
        {
            if (typed.Length == 0) return "";
            string[] candidates = new string[]
            {
                typed,                                                  // already the right folder
                Path.Combine(typed, "ph_ft", "work", "bin", "x64"),     // the game folder
                Path.Combine(typed, "work", "bin", "x64"),              // ph_ft
                Path.Combine(typed, "bin", "x64"),                      // work
                Path.Combine(typed, "x64")                              // bin
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

        // ---------------- toolbar ----------------


        /*
         * Adding a script copies it into scripts\ and lists it disabled.
         *
         * Copied rather than referenced in place, because the manifest names
         * scripts by plain filename and the runtime refuses anything with a
         * path -- referencing a file elsewhere would need one. It arrives
         * disabled because dropping a file in says nothing about whether it
         * should run, and with writes enabled that distinction can cost you a
         * save.
         */
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

        /*
         * Reload re-reads from disk. It is not how a change takes effect -- every
         * change saves immediately and Crane picks it up within a second -- and
         * the UX review assumed otherwise from this button's old prominence,
         * which is why it now sits in the toolbar with everything else.
         */
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
            /*
             * Through Steam when this is a Steam copy, never by running the exe.
             *
             * Starting DyingLightGame_TheBeast_x64_rwdi.exe directly gets as far
             * as a dialog reading "Steam client application is required in order
             * to play the game" and no further: the executable is DRM-wrapped and
             * expects to have been started by Steam, which sets up an environment
             * a plain CreateProcess does not. Setting the working directory --
             * which the old code was careful about -- does not help, because the
             * working directory was never the problem.
             *
             * The app id is a global Steam constant, the same on every install,
             * and was read from this machine's own
             * steamapps\appmanifest_3008130.acf ("Dying Light: The Beast") rather
             * than guessed.
             */
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
                    // Games resolve data relative to the working directory;
                    // inheriting this program's would be a subtle way to break a
                    // launch that otherwise looks fine.
                    start.WorkingDirectory = _folder;
                    if (route == GameLaunch.Route.DirectWithSteamEnv)
                    {
                        // Passing an environment requires UseShellExecute=false;
                        // with it true the dictionary is silently ignored, which
                        // would put us straight back to the DRM error with no
                        // sign of why.
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
            // Named as Steam's doing only when it is: that route takes several
            // seconds and shows its own progress, so the wording has to explain
            // the wait. The direct routes start immediately.
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

        /*
         * Moves the selected script within the manifest.
         *
         * Only listed entries move: an unlisted script has no manifest position
         * to change, and giving it one would write it into the manifest as a side
         * effect of pressing an arrow, which is not what the button says it does.
         */
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

            // Keep the moved script selected so a second press continues from
            // where the first left off, rather than the selection staying put and
            // the list appearing to move under it.
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

        /*
         * Remove deletes the script file.
         *
         * It used to remove the manifest entry and leave the .lua, which made
         * sense while the UI distinguished "listed" from "present". It does not
         * any more: every script in \scripts is shown, so removing an entry
         * would put the row straight back and Remove would appear to do nothing.
         *
         * Deleting is also what somebody means when they say remove this mod. It
         * asks first, and it uses the word delete rather than hiding behind
         * remove.
         */
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

        /*
         * Settings holds the plumbing the main window should not: where Crane is
         * installed, and how large this window should be drawn. Both are things
         * you set once and then stop thinking about, which is the test for
         * belonging here rather than on the main surface.
         */
        private void OnSettings(object sender, RoutedEventArgs e)
        {
            Window dialog = new Window();
            dialog.Title = "Settings";
            dialog.Owner = this;
            /*
             * Resizable, like the window it belongs to.
             *
             * SizeToContent plus NoResize was fine when this held two controls.
             * It now carries the Bridge's twelve settings with wrapping
             * descriptions, and a fixed dialog that decides its own height gets
             * that wrong on some screens with no way for the user to correct it.
             */
            dialog.ResizeMode = ResizeMode.CanResize;
            dialog.Width = 560;
            dialog.Height = 640;
            dialog.MinWidth = 380;
            dialog.MinHeight = 260;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(16);
            panel.MinWidth = 460;
            // The Bridge alone declares eleven settings, so this window can now
            // be taller than a small screen. Capped and scrolled rather than
            // left to SizeToContent, which would push the Close button off.
            ScrollViewer scroller = new ScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.Content = panel;

            /*
             * The scale setting applies here too.
             *
             * The main window scales through one LayoutTransform on its root, and
             * a dialog is a separate Window, so it never inherited it: somebody
             * running the UI at 150% opened Settings and got a 100% dialog, which
             * reads as the setting having stopped working.
             *
             * MaxHeight is applied in device-independent units before the
             * transform, so it is divided by the scale; otherwise a scaled dialog
             * would grow past the screen it was capped to fit.
             */
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
            // Editable, always. The WinForms window once shipped a read-only
            // path box beside an unreachable Browse button, leaving that screen
            // with no way in at all.
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
                // Rescale the open dialog as well, so the change is visible where
                // it is being made rather than only on the window behind it.
                scroller.LayoutTransform = new ScaleTransform(chosen, chosen);
            };
            panel.Children.Add(scale);

            /*
             * The platform's own settings, so the tool never asks anybody to
             * open an INI. CRANE's write switch in particular lived only in a
             * text file, which made the one setting with real consequences the
             * one setting the UI could not reach.
             *
             * One indented category per platform component. Flat, these ran to
             * thirteen settings under headings that looked exactly like the
             * group headings inside them, so "Console and log" and
             * "DLTBRuntimeBridge" read as siblings when one contains the other.
             * Nesting makes the ownership visible and lets somebody who came
             * here to change one thing not scroll past the other twelve.
             */
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

                // Indented under its own heading rather than collapsed behind a
                // control. Everything stays visible, and the indent is what says
                // "Console and log" belongs to the Bridge rather than sitting
                // beside it.
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

        /*
         * Finds Crane relative to wherever this program was started.
         *
         * It deploys to ph_ft and Crane lives in ph_ft\work\bin\x64, one level
         * down, and not beside the ASI: that folder's winmm.dll is Ultimate ASI
         * Loader and Windows resolves a DLL from the executable's own directory
         * first. A manager living there loads the loader, which injects every
         * .asi into the manager.
         *
         * Own directory is still checked first, so a copy dropped straight into
         * the Crane folder keeps working -- that is how the UI test sandbox runs.
         */
        private static string ResolveDefaultFolder()
        {
            string here = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrEmpty(here))
            {
                string[] relative = new string[]
                {
                    here,                                              // dropped in beside Crane
                    Path.Combine(here, "work", "bin", "x64"),          // deployed to ph_ft
                    Path.Combine(here, "ph_ft", "work", "bin", "x64")  // sitting at the game root
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
            // A report from a process that has exited describes nothing current,
            // however recently it was written. The age check stays as well: the
            // game can be running while Crane has not reported yet.
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

        /*
         * "Game: Connected" answers the question the old window could not: is any
         * of this reaching the game at all?
         *
         * Inferred from the status file's age, because Crane rewrites it on every
         * reload and cannot write it when the game is not running. A stale file is
         * therefore evidence of absence, and without this the user cannot tell a
         * broken script from a game that is not running.
         */
        private void UpdateConnection()
        {
            string status = Path.Combine(_folder, StatusFile.FileName);
            /*
             * Two facts, reported as three states, because conflating them is
             * what made this wrong twice.
             *
             *   is the game running  -- the process. Ground truth.
             *   has Crane reported   -- the status file's age.
             *
             * "Connected" was previously the second fact alone, so it went on
             * claiming a connection for up to five minutes after the game had
             * exited: a report written five seconds ago and a game that died
             * four minutes ago are indistinguishable by age alone. Asking about
             * the process makes the answer immediate and exact.
             *
             * The middle state earns its own wording rather than being folded
             * into either neighbour, because between clicking Launch and Crane's
             * first report there are several seconds in which neither "not
             * running" nor "connected" is true.
             */
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

        /*
         * The detail pane reads top to bottom like a document.
         *
         * Title, subtitle, prose, then a section heading, then settings in the
         * settings-list idiom: label flush left, value flush right, description
         * indented beneath. That is the inverse of the WinForms window, where
         * labels were right-aligned in a column before their controls, and it is
         * what makes a long list of settings scannable.
         */
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

            // The filename is implementation metadata. It belongs here, small,
            // rather than taking up a column in the list.
            TextBlock file = new TextBlock();
            file.Text = entry.File;
            file.Foreground = (Brush)FindResource("InkFaint");
            file.Margin = new Thickness(0, 1, 0, 0);
            Detail.Children.Add(file);

            // A failure goes above everything. Somebody whose script just failed
            // does not want to tune it.
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

            /*
             * Settings are grouped under the headings their author declared.
             *
             * Groups appear in order of first declaration rather than
             * alphabetically. The author wrote them in an order, that order
             * usually means something -- the main lever first, the fiddly bits
             * after -- and sorting would throw away information the manager
             * cannot recover. Same reason the script list is unsorted.
             *
             * Ungrouped settings keep the plain "Settings" heading and come
             * first, so a script that declares no groups renders exactly as it
             * did before this existed. Adding a grammar option must not change
             * how existing scripts look.
             */
            // Built by hand rather than collected and sorted: List.Sort is
            // introsort and is not stable, so a comparator that called two
            // groups equal would be free to reorder them, silently undoing the
            // declaration order this is trying to preserve.
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

            // Pane-scoped, bottom right, per the mockup. Nearly free: the
            // declared default is already parsed for every parameter.
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
            /*
             * Load order, restored.
             *
             * The WinForms window had Move up and Move down; the WPF port
             * silently dropped them, and nothing noticed because order is
             * invisible until two scripts want the same thing.
             *
             * Leases are exclusive per path, so when two enabled scripts claim
             * one path the higher manifest entry wins and the lower is refused.
             * Without these buttons the only way to decide which script wins was
             * to hand-edit the manifest, which is the thing this tool exists to
             * avoid.
             */
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

        /*
         * The client detail pane, in the same document shape as a script's.
         *
         * Two things are said here that are not said for a script, because they
         * are true here and not there:
         *
         *   - the INI filename, because the user may well have edited this file
         *     by hand and needs to know it is the same one;
         *   - when a change takes effect. A Lua script reloads within a second;
         *     a native client re-reads its INI on its own schedule, and several
         *     only do so at startup. Implying otherwise would have people
         *     changing a value and concluding the tool is broken.
         */
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

            /*
             * The enable key gets a control even when the INI only marked it with
             * @enable and never declared it as a @param.
             *
             * Removing the row checkbox would otherwise make a client's own off
             * switch unreachable from the tool whose entire purpose is not having
             * to open the file. Synthesised rather than required, so no INI can
             * declare a switch this window cannot show.
             */
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

        /*
         * Renders a client's declared settings into any panel.
         *
         * Shared by the detail pane and the Settings window, because the Bridge
         * and CRANE's own INIs are edited with exactly the same machinery as a
         * client's. Only where they render differs: they are part of the
         * platform rather than mods you install, so they belong under Settings
         * rather than in a list of clients.
         *
         * Groups keep declaration order and the ungrouped-first bucket. See
         * BuildRows' note on why nothing here is sorted.
         */
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

        /*
         * The platform's own INIs: CRANE's and the Bridge's.
         *
         * Excluded from the Bridge-clients list because neither is a client. The
         * Bridge is what clients load through, and CRANE is the tool's own
         * runtime; listing either beside Time Lapse would say they are the same
         * kind of thing.
         */
        private static readonly string[] PlatformInis = new string[]
        {
            "DLTBRuntimeCrane.ini",
            "DLTBRuntimeBridge.ini",
        };

        /*
         * Reads a platform INI, supplying CRANE's own declarations in code.
         *
         * The Bridge's INI is packaged, so its annotations arrive with an update
         * and reading them back is right. CRANE's is generated once on first run
         * and never rewritten, so an install made before the annotations existed
         * has a file that declares nothing, and the Settings window showed no
         * CRANE section at all -- which is how "Allow scripts to change the
         * game" came to be missing from a working install.
         *
         * Declaring it here rather than migrating the file: the manager ships
         * with CRANE and always knows CRANE's own settings, so a round trip
         * through the user's file buys nothing and rewriting their config to add
         * comments is a side effect nobody asked for. The file's annotations stay
         * in the template because they document the file for anyone reading it.
         */
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
            /*
             * Read-only rather than hidden when the manager cannot safely write.
             *
             * Both cases are the mod author's mistake, not the user's, and both
             * are worth seeing: a declared key missing from the file, and a key
             * that appears twice so writing it would be a guess. Hiding the row
             * would leave the user wondering where a documented setting went.
             */
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
            // Committed on leaving the field rather than per keystroke: this
            // writes to somebody else's config file, and "1" on the way to "119"
            // is not a value anybody asked to store.
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
                // Bounds are the author's declaration, and the client enforces
                // its own anyway -- so this clamps and says so rather than
                // refusing, which would lose what the user was reaching for.
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

            // The range belongs under the field it constrains, not stranded in a
            // far column. "on/off" is gone entirely: the checkbox says it.
            string range = decl.Type == ParamType.Number && (decl.HasMin || decl.HasMax)
                ? string.Format(CultureInfo.InvariantCulture, "{0} to {1}", decl.Min, decl.Max)
                : "";

            /*
             * One line under the control, carrying the description and the range
             * together rather than stacking two faint lines.
             *
             * The description leads because it answers "what is this", which is
             * the question somebody has before "what will it accept". The range
             * follows in parentheses, and on its own when there is no
             * description, which is what this line used to be, so scripts that
             * declare no desc= look unchanged.
             */
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
            // 25, not 25.000. A whole-number range gets no decimals at all; the
            // old window hardcoded three for everything, which made a percentage
            // read like a raw API value.
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

        /*
         * Counts the rows on screen, where a row means a file in \scripts.
         *
         * It counted manifest entries instead, so a list of four reported "2
         * scripts ... 2 not yet configured", and an earlier version managed "1
         * script - 0 enabled" beside a list of four. Both numbers were true and
         * neither answered the user's question; whether a script has a manifest
         * entry yet is the manager's own bookkeeping, and saying it out loud
         * only invited the reader to wonder what it meant.
         */
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
