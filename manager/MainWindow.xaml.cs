// SPDX-License-Identifier: GPL-3.0-only
// CraneLoader -- main window.
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
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace CraneLoader
{
    public partial class MainWindow : Window
    {
        /*
         * Settings sit indented under their section heading, so the heading is
         * the only thing on its own edge and the grouping survives scrolling.
         * Matches GapWide in App.xaml.
         */
        private const double SettingIndent = 16;

        /* A hairline under a section heading rather than a card around the
           section, so the pane keeps reading top to bottom. */
        private Border SectionRule()
        {
            Border rule = new Border();
            rule.Height = 1;
            rule.SetResourceReference(Border.BackgroundProperty, "Rule");
            rule.Margin = new Thickness(0, 0, 0, 10);
            return rule;
        }

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
            WindowChrome.Apply(this, Theme.Find(Theme.Current).Dark);
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
            _reportWasFresh = Reporting();
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
        /*
         * One question, asked in one place: is Crane's report describing the
         * game currently running?
         *
         * It was written out longhand at three call sites, and the comment at
         * each said "the same conjunction the others use", which is what a
         * shared method is for. They agreed by discipline until the test itself
         * had to change, at which point three separate edits was three chances
         * to leave one behind saying something different.
         */
        private bool Reporting()
        {
            GameLaunch.GameProcess game = GameLaunch.Find();
            return game.Running && StatusFile.DescribesCurrentSession(_folder, game.StartedUtc);
        }

        private void OnHeartbeat()
        {
            // The same test LoadMetadata uses. Keying the edge on file age
            // alone would leave the dots lit for five minutes after the game
            // exited -- and, once Crane stopped rewriting the file during a
            // session, would blank them five minutes into one.
            bool fresh = Reporting();
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
            // Before any row is built, because the row's own second line reports
            // the result and a row built from a stale calculation would name the
            // wrong winner.
            ClaimMap.Compute(_entries);

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

            List<ScriptRow> unlisted = new List<ScriptRow>();
            foreach (string file in UnlistedScripts())
            {
                ScriptEntry entry = new ScriptEntry();
                entry.File = file;
                entry.Enabled = false;
                string name, description;
                List<ParamDecl> declared;
                List<ClaimDecl> claims;
                ScriptHeader.Read(Path.Combine(_folder, "scripts", file),
                                  out name, out description, out declared, out claims);
                entry.Name = name;
                entry.Description = description;
                entry.Declared = declared;
                entry.Claims = claims;
                entry.State = ScriptState.Unknown;
                unlisted.Add(new ScriptRow(entry, OnUnlistedToggled, true));
            }

            /*
             * Listed scripts stay in manifest order -- which is load order --
             * and the unlisted ones follow, sorted among themselves.
             *
             * 2.3.0 sorted the whole list alphabetically, on the argument that a
             * script's position encoded two things at once and served neither, so
             * finding a script by name should win. Load order is what the user can
             * change here, and it decides which of two scripts gets a contested
             * lease, so a list that does not show it leaves the manager stating the
             * order in prose instead: "loads 4 of 9, but not where you see it".
             *
             * Position means load order again, and it is editable in place: drag a
             * row, or use the detail pane's two buttons.
             *
             * The unlisted tail is sorted because those scripts genuinely have no
             * order: they are not in the manifest, so there is no arrival order
             * to preserve and nothing a position could mean. Ordinal-insensitive
             * rather than culture-aware, so it does not reshuffle itself on a
             * machine with a different locale.
             *
             * Only the tail is sorted, and it is sorted separately rather than
             * by a comparer that returns 0 for two listed rows: List.Sort is
             * introsort and is not stable, so "compares equal" would leave the
             * load order to the sort's partitioning, differently on each
             * rebuild.
             */
            unlisted.Sort(delegate(ScriptRow left, ScriptRow right)
            {
                int byName = string.Compare(left.Name, right.Name,
                                            StringComparison.OrdinalIgnoreCase);
                // Two scripts can declare the same @name -- a copy taken for
                // experiments is the usual way -- and a comparison that called
                // them equal would leave their order down to the sort, which is
                // to say arbitrary and unstable between rebuilds.
                return byName != 0
                    ? byName
                    : string.Compare(left.File, right.File, StringComparison.OrdinalIgnoreCase);
            });
            rows.AddRange(unlisted);

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

        /*
         * A tick moves the row to the near side of the enabled/disabled
         * boundary, so the list always reads enabled-first.
         *
         * Newly enabled means "loads last", which is the conservative default:
         * everything already running keeps the leases it holds, and the new
         * script is the one refused if it collides. The alternative -- leave it
         * where it sat while disabled -- silently promotes a script above
         * working ones on the strength of where it happened to be in a file.
         *
         * The move happens before Save so the manifest and the list agree, and it
         * forces a full rebuild rather than the in-place refresh below, because the
         * row changes position and RefreshConflicts cannot express that.
         */
        private void OnRowToggled(ScriptRow row)
        {
            if (LoadOrder.Regroup(_entries, row.Entry))
            {
                Save();
                BuildRows();
                // Follow the row that moved. It jumped several places under the
                // cursor, and a selection left behind on whatever slid into its
                // old slot would look like the click landed on the wrong script.
                foreach (object item in (System.Collections.IEnumerable)ScriptList.ItemsSource)
                {
                    ScriptRow candidate = item as ScriptRow;
                    if (candidate != null && ReferenceEquals(candidate.Entry, row.Entry))
                    {
                        ScriptList.SelectedItem = candidate;
                        break;
                    }
                }
                UpdateSummary();
                return;
            }
            Save();
            /*
             * Ticking one script changes what every other script gets.
             *
             * This is the case the feature was asked for: Quick Hands is already
             * on, Steady Hands is ticked, and the row that has to change is
             * Steady Hands' -- but turning Quick Hands OFF has to clear the
             * warning on Steady Hands too, and that is a row nobody touched.
             * A rebuild would resort the list under the cursor at the moment of
             * a click, so instead the calculation is redone and the existing
             * rows are asked to reread it.
             */
            RefreshConflicts(true);
            UpdateSummary();
        }

        /*
         * `redrawDetail` is false when the change came from inside the detail
         * pane itself. Rebuilding the pane under the user's hands would take the
         * focus out of the control they are still editing -- a slider being
         * dragged, a number half typed -- and it is the same hazard
         * DetailHasFocus already guards elsewhere.
         */
        private void RefreshConflicts(bool redrawDetail)
        {
            ClaimMap.Compute(_entries);
            System.Collections.IEnumerable rows = ScriptList.ItemsSource as System.Collections.IEnumerable;
            if (rows != null)
                foreach (object item in rows)
                {
                    ScriptRow candidate = item as ScriptRow;
                    if (candidate != null) candidate.RaiseAll();
                }
            // The detail pane states the conflict in full, so it goes stale for
            // the same reason the rows do.
            if (!redrawDetail) return;
            ScriptRow selected = ScriptList.SelectedItem as ScriptRow;
            if (selected != null && !DetailHasFocus()) ShowDetail(selected);
        }

        // Ticking an unlisted script is how it joins the list. The file turning
        // up in the folder is not enough on its own.
        /*
         * An unlisted script joins the manifest at the end of the enabled run,
         * not at the end of the file, which is where OnRowToggled puts a script
         * that was already listed. "Tick it and it loads last" has to mean the same
         * thing however the script got here.
         */
        private void OnUnlistedToggled(ScriptRow row)
        {
            if (!row.Enabled) return;
            _entries.Insert(LoadOrder.Boundary(_entries, null), row.Entry);
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
                                        "CraneLoader", MessageBoxButton.YesNo,
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
            if (row == null || !Draggable(row)) return;
            // Boundary, not EnabledCount: this compares against an index. See
            // the note on EnabledCount in LoadOrder.cs.
            if (delta > 0 && _entries.IndexOf(row.Entry) >= LoadOrder.Boundary(_entries, null) - 1)
                return;
            int from = _entries.IndexOf(row.Entry);
            if (from < 0) return;
            int to = from + delta;
            if (to < 0 || to >= _entries.Count) return;

            ScriptEntry moved = row.Entry;
            // Through LoadOrder, same as the drag. `to` here is a final position
            // rather than a pre-removal target, and for a one-step move the two
            // differ only when stepping down -- so hand it the pre-removal form.
            if (!LoadOrder.Move(_entries, moved, delta > 0 ? to + 1 : to)) return;
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

        /* ---------------------------------------------------------------- */
        /* Dragging a row to change the load order                           */
        /* ---------------------------------------------------------------- */

        /*
         * The list is the load order, so the direct way to change the load order
         * is to move a row. The Load earlier / Load later buttons stay, because
         * a drag is a mouse-only gesture and reordering must not become
         * mouse-only with it.
         *
         * Three things this refuses to do:
         *
         *   - drag an unlisted script. It has no manifest position, and giving
         *     it one by dropping it would write it into the manifest as a side
         *     effect of a gesture that says nothing about being enabled. Same
         *     reasoning as MoveSelected above.
         *   - start a drag from the checkbox. The tick is the most-used control
         *     on the row; a drag that begins there would swallow clicks that
         *     were meant to enable a script.
         *   - reorder anything on a drop that changes nothing. A no-op that
         *     still says "moved" and still writes the manifest teaches the user
         *     that the readout is not to be trusted.
         */
        private Point _dragOrigin;
        private ScriptRow _dragCandidate;
        private DropLineAdorner _dropLine;
        private ListBoxItem _dropLineRow;
        private bool _dropLineBelow;
        private DragChipAdorner _dragChip;

        private void OnScriptListMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragCandidate = null;
            DependencyObject source = e.OriginalSource as DependencyObject;
            if (Ancestor<CheckBox>(source) != null) return;
            ListBoxItem item = Ancestor<ListBoxItem>(source);
            if (item == null) return;
            ScriptRow row = item.DataContext as ScriptRow;
            if (row == null || !Draggable(row)) return;
            _dragOrigin = e.GetPosition(null);
            _dragCandidate = row;
        }

        private void OnScriptListMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragCandidate == null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { _dragCandidate = null; return; }

            // The system drag threshold, not a number of our own: below it the
            // gesture is still a click, and a list whose rows detach on a
            // one-pixel wobble is unusable for selecting.
            Point now = e.GetPosition(null);
            if (Math.Abs(now.X - _dragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(now.Y - _dragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            ScriptRow dragging = _dragCandidate;
            _dragCandidate = null;
            try
            {
                DragDrop.DoDragDrop(ScriptList, dragging, DragDropEffects.Move);
            }
            finally
            {
                // DoDragDrop blocks until the drop resolves, and the adorners must
                // go whichever way it ended -- dropped, cancelled with Escape, or
                // released outside the window.
                ClearDragVisuals();
            }
        }

        /*
         * Left unhandled unless the payload is one of our own rows.
         *
         * Drop and DragOver bubble, and the window above this list already
         * handles them: dropping a .lua file onto the list is how a script is
         * added. Marking every drag handled here would have removed that, and the
         * list is the obvious place to drop a script.
         */
        private void OnScriptListDragOver(object sender, DragEventArgs e)
        {
            int to;
            ScriptRow dragged = e.Data.GetData(typeof(ScriptRow)) as ScriptRow;
            if (dragged == null) return;
            ShowDragChip(dragged.Name, e.GetPosition(ScriptList));
            e.Effects = DropIndex(e, dragged, out to)
                ? DragDropEffects.Move
                : DragDropEffects.None;
            e.Handled = true;
        }

        /*
         * There is no edge auto-scroll here.
         *
         * dev.3 had one and it made the list unusable: dragging slammed it to
         * the top or the bottom and stayed there. Two mistakes compounded.
         *
         * The first is a unit error. A ListBox scrolls by item, since its
         * ScrollViewer has CanContentScroll true, so ViewportHeight,
         * ExtentHeight and ScrollableHeight are counts of rows, not pixels.
         * Comparing a pixel Y against `ViewportHeight - 24` compared a
         * coordinate to a row count: for any list shorter than 24 rows the
         * threshold is negative, so the "near the bottom edge" test was true
         * everywhere in the list.
         *
         * The second is the event rate. DragOver repeats continuously while the
         * pointer is inside the drop target, stationary or not, so "one line per
         * DragOver" is not the nudge the old comment claimed -- it is as many
         * lines as the machine can raise events, which scrolls the whole list.
         *
         * It survived the offline check because it is inert wherever it is
         * harmless: with every row visible ScrollableHeight is zero and LineDown
         * does nothing, so a list that fits its window shows no symptom. The list
         * it was checked against fit; the one it shipped to did not.
         *
         * Reordering across a list taller than the window is done with Load
         * earlier / Load later, or by dropping at the visible edge and dragging
         * again. If that becomes the common case, an auto-scroll belongs here --
         * measured in pixels off ActualHeight, rate-limited, and checked against
         * a list long enough to actually scroll.
         */

        private void OnScriptListDragLeave(object sender, DragEventArgs e)
        {
            // Both go, including the chip: it is anchored to the list, so it
            // would otherwise sit frozen at the edge while the pointer is over
            // the detail pane. DragOver puts it back if the pointer returns.
            ClearDragVisuals();
        }

        private void OnScriptListDrop(object sender, DragEventArgs e)
        {
            ClearDragVisuals();
            ScriptRow dragged = e.Data.GetData(typeof(ScriptRow)) as ScriptRow;
            // Not ours: let it bubble to the window, which adds dropped .lua
            // files. See OnScriptListDragOver.
            if (dragged == null) return;
            e.Handled = true;
            if (!Ready()) return;

            int to;
            if (!DropIndex(e, dragged, out to)) return;
            ScriptEntry moved = dragged.Entry;
            // LoadOrder owns the arithmetic, including refusing the two targets
            // that mean "leave it where it is". See LoadOrder.cs.
            if (!LoadOrder.Move(_entries, moved, to)) return;
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
            StatusText.Text = string.Format(CultureInfo.InvariantCulture,
                                            "{0} now loads {1} of {2}.",
                                            moved.Name,
                                            LoadOrder.PositionOf(_entries, moved) + 1,
                                            _entries.Count);
        }

        /*
         * Where the pointer says the row should go, as an index into _entries,
         * and the insertion line that says so on screen.
         *
         * The index is resolved through the row under the pointer rather than
         * from its position in the list, because the list hides entries whose
         * file has gone missing -- so list position and manifest position are
         * not the same number, and only the manifest one can be inserted into.
         *
         * Returns false when there is nowhere valid to drop, which is what makes
         * the cursor show a refusal rather than a silent no-op on release.
         */
        /*
         * Only an enabled, listed script drags.
         *
         * An unlisted one has no manifest position to change. A disabled one has
         * a position, but not a meaningful one: load order decides who wins a
         * contested lease, and a script that is switched off claims nothing at
         * all. Dragging it into the middle of the enabled run would place a row
         * whose position carries no meaning among rows whose position does, and
         * undo the arrangement a tick maintains.
         */
        private static bool Draggable(ScriptRow row)
        {
            return row != null && !row.Unlisted && row.Entry.Enabled;
        }

        private bool DropIndex(DragEventArgs e, ScriptRow dragged, out int to)
        {
            to = -1;
            if (!Draggable(dragged)) return false;

            // The floor of the disabled block, as an index into _entries. Not
            // the number of enabled entries, which is the same only when the run
            // starts at the top. See LoadOrder.EnabledCount.
            int limit = LoadOrder.Boundary(_entries, null);

            ListBoxItem row = Ancestor<ListBoxItem>(e.OriginalSource as DependencyObject);
            ScriptRow over = row == null ? null : row.DataContext as ScriptRow;

            /*
             * Below the enabled run -- over a disabled row, over the unlisted
             * tail, or past the last row entirely -- the drop means "last among
             * the enabled". Clamped rather than refused: "drag it to the bottom"
             * is the obvious way to ask for exactly that, and a cursor that
             * simply says no leaves the user guessing which part of the list is
             * a valid target.
             */
            if (over == null || over.Unlisted || !over.Entry.Enabled)
            {
                to = limit;
                ShowDropLine(LastEnabledRow(), true);
                return true;
            }

            // Upper half inserts above the row under the pointer, lower half
            // below it. Anything else makes the line jump a row away from where
            // the pointer is, which reads as a bug.
            bool below = e.GetPosition(row).Y > row.ActualHeight / 2;
            ShowDropLine(row, below);
            to = _entries.IndexOf(over.Entry);
            if (to < 0) return false;
            if (below) to++;
            return true;
        }

        /* The container of the last enabled row, so the clamped drop can draw
           its line on a real row rather than nowhere. Null when nothing is
           enabled, which is a list with no valid drag in it anyway. */
        private ListBoxItem LastEnabledRow()
        {
            ListBoxItem last = null;
            System.Collections.IEnumerable rows = ScriptList.ItemsSource as System.Collections.IEnumerable;
            if (rows == null) return null;
            foreach (object item in rows)
            {
                ScriptRow row = item as ScriptRow;
                if (row == null || row.Unlisted || !row.Entry.Enabled) continue;
                ListBoxItem container =
                    ScriptList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                if (container != null) last = container;
            }
            return last;
        }

        private void ShowDropLine(ListBoxItem row, bool below)
        {
            // No row to draw on: nothing is enabled, or its container has been
            // virtualised away. The drop is still valid, so this clears the line
            // rather than refusing.
            if (row == null) { ClearDropLine(); return; }
            if (ReferenceEquals(_dropLineRow, row) && _dropLineBelow == below && _dropLine != null)
                return;
            ClearDropLine();
            AdornerLayer layer = AdornerLayer.GetAdornerLayer(row);
            if (layer == null) return;
            _dropLine = new DropLineAdorner(row, below, ThemeBrush("Accent", Brushes.Gray));
            layer.Add(_dropLine);
            _dropLineRow = row;
            _dropLineBelow = below;
        }

        /*
         * The name of the script, following the cursor.
         *
         * The insertion line says where the row will land; it does not say what
         * is being moved. Once the pointer has travelled a few rows the row it
         * started on is no longer under it and no longer obviously the subject,
         * and on a list of nine similar-looking entries that is a real question.
         * Carrying the name answers it without the user having to remember what
         * they picked up.
         *
         * The chip is drawn, not a rendered copy of the row. A ghost of the whole
         * row would bring its checkbox and its two status dots along, and a
         * floating checkbox under the cursor invites a click that cannot happen.
         */
        private void ShowDragChip(string name, Point at)
        {
            if (_dragChip == null)
            {
                AdornerLayer layer = AdornerLayer.GetAdornerLayer(ScriptList);
                if (layer == null) return;
                _dragChip = new DragChipAdorner(ScriptList, name,
                                                ThemeBrush("SurfaceRaised", Brushes.White),
                                                ThemeBrush("RuleStrong", Brushes.Gray),
                                                ThemeBrush("Ink", Brushes.Black));
                layer.Add(_dragChip);
            }
            _dragChip.MoveTo(at);
        }

        private void ClearDragVisuals()
        {
            ClearDropLine();
            ClearDragChip();
        }

        private void ClearDragChip()
        {
            if (_dragChip != null)
            {
                AdornerLayer layer = AdornerLayer.GetAdornerLayer(ScriptList);
                if (layer != null) layer.Remove(_dragChip);
                _dragChip = null;
            }
        }

        private static Brush ThemeBrush(string key, Brush fallback)
        {
            Brush found = Application.Current != null
                ? Application.Current.TryFindResource(key) as Brush
                : null;
            return found ?? fallback;
        }

        private void ClearDropLine()
        {
            if (_dropLine != null && _dropLineRow != null)
            {
                AdornerLayer layer = AdornerLayer.GetAdornerLayer(_dropLineRow);
                if (layer != null) layer.Remove(_dropLine);
            }
            _dropLine = null;
            _dropLineRow = null;
        }

        /*
         * Walks up from whatever the mouse actually hit -- usually a TextBlock
         * inside the row template -- to the thing being asked about.
         *
         * Visual first, logical as the fallback: a hit can land on a run of text
         * or a content element that is not a Visual, and VisualTreeHelper throws
         * rather than returning null for those. Same shape as the focus walk in
         * DetailHasFocus.
         */
        private static T Ancestor<T>(DependencyObject from) where T : DependencyObject
        {
            while (from != null)
            {
                T found = from as T;
                if (found != null) return found;
                DependencyObject parent = null;
                if (from is Visual || from is System.Windows.Media.Media3D.Visual3D)
                    parent = VisualTreeHelper.GetParent(from);
                if (parent == null) parent = LogicalTreeHelper.GetParent(from);
                from = parent;
            }
            return null;
        }

        /*
         * The insertion line: two pixels of Accent along the edge the row would
         * be inserted at.
         *
         * An adorner rather than a Border added to the template, because it has
         * to sit on top of a row it is not part of, and it must not participate
         * in hit testing -- an indicator that intercepts the drop it exists to
         * describe would be its own bug.
         */
        /*
         * The name chip: a small label that follows the pointer for the length
         * of the drag.
         *
         * FormattedText rather than a hosted TextBlock. An adorner that hosts
         * real elements has to run its own measure and arrange pass and keep a
         * VisualCollection in step, which is a lot of machinery for one line of
         * static text that never wraps, never takes focus and never changes
         * after it is created.
         *
         * The text is measured once in the constructor, because the size decides
         * where the chip is allowed to sit and that question is asked on every
         * pointer move.
         */
        private sealed class DragChipAdorner : Adorner
        {
            private const double PadX = 9;
            private const double PadY = 5;
            /* Offset down and right of the pointer, so the chip trails the
               cursor instead of sitting under it and hiding the insertion line
               it is meant to accompany. */
            private const double OffsetX = 16;
            private const double OffsetY = 10;

            private readonly FormattedText _text;
            private readonly Brush _fill;
            private readonly Pen _edge;
            private readonly Size _size;
            private Point _at;

            public DragChipAdorner(UIElement adorned, string name,
                                   Brush fill, Brush edge, Brush ink)
                : base(adorned)
            {
                _fill = fill;
                _edge = new Pen(edge, 1);
                _edge.Freeze();
                // The DPI-aware overload: the one without it is obsolete and
                // renders at the wrong size on a scaled display, which is most
                // of them.
                _text = new FormattedText(
                    string.IsNullOrEmpty(name) ? "script" : name,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    12,
                    ink,
                    VisualTreeHelper.GetDpi(adorned).PixelsPerDip);
                _size = new Size(_text.Width + PadX * 2, _text.Height + PadY * 2);
                IsHitTestVisible = false;
            }

            public void MoveTo(Point at)
            {
                _at = at;
                InvalidateVisual();
            }

            protected override void OnRender(DrawingContext drawing)
            {
                Rect within = new Rect(AdornedElement.RenderSize);
                double x = _at.X + OffsetX;
                double y = _at.Y + OffsetY;
                // Kept inside the list. Near the right edge the chip would
                // otherwise hang over the detail pane, where it reads as part of
                // the pane rather than as something being carried.
                if (x + _size.Width > within.Right) x = _at.X - OffsetX - _size.Width;
                if (y + _size.Height > within.Bottom) y = within.Bottom - _size.Height;
                if (x < within.Left) x = within.Left;
                if (y < within.Top) y = within.Top;

                Rect chip = new Rect(x, y, _size.Width, _size.Height);
                drawing.PushOpacity(0.95);
                drawing.DrawRoundedRectangle(_fill, _edge, chip, 3, 3);
                drawing.DrawText(_text, new Point(x + PadX, y + PadY));
                drawing.Pop();
            }
        }

        private sealed class DropLineAdorner : Adorner
        {
            private readonly bool _below;
            private readonly Pen _pen;

            public DropLineAdorner(UIElement adorned, bool below, Brush brush)
                : base(adorned)
            {
                _below = below;
                _pen = new Pen(brush, 2);
                _pen.Freeze();
                IsHitTestVisible = false;
            }

            protected override void OnRender(DrawingContext drawing)
            {
                Rect area = new Rect(AdornedElement.RenderSize);
                // Inset by half the stroke so the line lands inside the row
                // rather than straddling its edge and being clipped in half.
                double y = _below ? area.Bottom - 1 : area.Top + 1;
                drawing.DrawLine(_pen, new Point(area.Left, y), new Point(area.Right, y));
            }
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
                    "CraneLoader", MessageBoxButton.YesNo,
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

        /*
         * Scale the window, and pick the text rendering to match.
         *
         * Display mode snaps glyphs to whole pixels, which is right at 100%,
         * where a device-independent unit is a pixel, and wrong under a
         * transform, where that grid is no longer the one being drawn on. Ideal
         * keeps the typeface's proportions and lets the transform scale them.
         * TextOptions inherits, so the root covers every element below it.
         */
        internal static TextFormattingMode FormattingFor(double scale)
        {
            return Math.Abs(scale - 1.0) < 0.001
                ? TextFormattingMode.Display : TextFormattingMode.Ideal;
        }

        private void ApplyScale(double scale)
        {
            RootScale.ScaleX = scale;
            RootScale.ScaleY = scale;
            TextOptions.SetTextFormattingMode(Root, FormattingFor(scale));
            // Snapping layout to whole pixels fights a fractional transform for
            // the same reason.
            UseLayoutRounding = Math.Abs(scale - 1.0) < 0.001;
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
            // A separate Window inherits the application's resources but not the
            // main window's own properties, so the surface and the chrome both
            // have to be asked for again here.
            dialog.SetResourceReference(Window.BackgroundProperty, "Surface");
            WindowChrome.Apply(dialog, Theme.Find(Theme.Current).Dark);
            /*
             * Resizable, like the window it belongs to.
             *
             * SizeToContent plus NoResize was fine when this held two controls.
             * It now carries the Bridge's twelve settings with wrapping
             * descriptions, and a fixed dialog that decides its own height gets
             * that wrong on some screens with no way for the user to correct it.
             */
            dialog.ResizeMode = ResizeMode.CanResize;

            /*
             * The dialog's own size scales with the UI scale. The contents are
             * scaled by a LayoutTransform below, but these four numbers are
             * device-independent units and were not, so above 100% the content
             * needed more width than the window had and the fields were cut off.
             * Present in 2.1.1 and earlier.
             *
             * Capped to the work area, because the scale goes to 200% and a
             * dialog larger than the screen is a worse failure than the one
             * being fixed.
             */
            double zoom = Settings.LoadScale();
            dialog.Width = Math.Min(560 * zoom, SystemParameters.WorkArea.Width);
            dialog.Height = Math.Min(640 * zoom, SystemParameters.WorkArea.Height);
            dialog.MinWidth = Math.Min(380 * zoom, SystemParameters.WorkArea.Width);
            dialog.MinHeight = Math.Min(260 * zoom, SystemParameters.WorkArea.Height);
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            StackPanel panel = new StackPanel();
            panel.Margin = new Thickness(16);
            /*
              * Nothing here declares a width, deliberately.
              *
              * A ScrollViewer with horizontal scrolling disabled measures its
              * content at exactly the viewport width, which is correct at every
              * window size and UI scale. Three attempts to improve on that each
              * made it worse: MinWidth stopped the panel shrinking and clipped
              * the right edge; enabling horizontal scrolling measured content at
              * infinite width and stopped TextWrapping working; and subtracting
              * a constant from ActualWidth was wrong by a few pixels at every
              * size, because border and scrollbar metrics vary by theme and DPI.
              */
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
             * The dialog's own dimensions are scaled by the same factor where it
             * is constructed above, because a LayoutTransform grows what the
             * content asks for without telling the window to make room for it.
             */
            scroller.LayoutTransform = new ScaleTransform(zoom, zoom);
            TextOptions.SetTextFormattingMode(scroller, FormattingFor(zoom));

            /*
             * Disabled: these are responsive rows, not a wide canvas. Allowing
             * horizontal scroll would remove the width constraint that wrapping
             * depends on. The narrow case is handled by dialog.MinWidth above.
             */
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

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

            /*
             * Theme, above UI scale because it is the one people go looking for.
             *
             * Applied on selection rather than behind an OK button: a palette is
             * judged by looking at it, and a picker that needs confirmation
             * before you can see the result makes choosing one a guessing game.
             * The change repaints both windows immediately, this dialog
             * included, so the effect is visible where it is being made.
             */
            TextBlock themeLabel = new TextBlock();
            themeLabel.Text = "Theme";
            themeLabel.Style = (Style)FindResource("SectionHeading");
            panel.Children.Add(themeLabel);

            ComboBox themes = new ComboBox();
            foreach (ThemeDefinition definition in Theme.All)
            {
                ComboBoxItem item = new ComboBoxItem();
                item.Content = definition.Name;
                item.Tag = definition;
                themes.Items.Add(item);
                if (definition.Name == Theme.Current) themes.SelectedItem = item;
            }
            TextBlock themeHint = new TextBlock();
            themeHint.Style = (Style)FindResource("Subtle");
            themeHint.FontSize = 11;
            themeHint.Margin = new Thickness(0, 4, 0, 0);
            themeHint.Text = Theme.Find(Theme.Current).Summary;
            themes.SelectionChanged += delegate
            {
                ComboBoxItem item = themes.SelectedItem as ComboBoxItem;
                if (item == null) return;
                ThemeDefinition chosen = (ThemeDefinition)item.Tag;
                Theme.Apply(chosen.Name);
                Settings.SaveTheme(chosen.Name);
                themeHint.Text = chosen.Summary;
            };
            panel.Children.Add(themes);
            panel.Children.Add(themeHint);

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
                TextOptions.SetTextFormattingMode(scroller, FormattingFor(chosen));
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
                inner.Margin = new Thickness(SettingIndent, 0, 0, 0);

                /*
                 * A platform is a stronger division than the category headings
                 * beneath it, so it gets the display face and a rule of its own.
                 * CRANE's rule is teal and the Bridge's is neutral: this tool is
                 * CRANE's, and the Bridge is the runtime it happens to be able to
                 * configure. Making both rules identical said they were peers.
                 */
                bool isCrane = platform.Name.IndexOf("crane",
                    StringComparison.OrdinalIgnoreCase) >= 0;

                TextBlock header = new TextBlock();
                header.Text = platform.Name.ToUpperInvariant();
                header.FontFamily = (FontFamily)FindResource("DisplayFont");
                header.FontWeight = FontWeights.SemiBold;
                header.FontSize = 13;
                header.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
                header.Margin = new Thickness(0, 18, 0, 4);

                Border rule = new Border();
                rule.Height = isCrane ? 2 : 1;
                rule.SetResourceReference(Border.BackgroundProperty,
                                          isCrane ? "Accent" : "Rule");
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
            // however recently it was written; and one written before this run
            // of the game started describes the previous run. Both are the same
            // comparison, so both live in DescribesCurrentSession.
            GameLaunch.GameProcess game = GameLaunch.Find();
            StatusReport report = game.Running ? StatusFile.Read(_folder, game.StartedUtc) : null;
            foreach (ScriptEntry entry in _entries)
            {
                string script = Path.Combine(_folder, "scripts", entry.File);
                entry.Missing = !File.Exists(script);
                string name, description;
                List<ParamDecl> declared;
                List<ClaimDecl> claims;
                if (entry.Missing)
                {
                    name = entry.File;
                    description = "Not present in \\scripts";
                    declared = new List<ParamDecl>();
                    claims = new List<ClaimDecl>();
                }
                else
                {
                    ScriptHeader.Read(script, out name, out description, out declared, out claims);
                }
                entry.Name = name;
                entry.Description = description;
                entry.Declared = declared;
                entry.Claims = claims;
                entry.State = report == null ? ScriptState.Unknown : report.StateOf(entry.File);
                entry.StateError = report == null ? "" : report.ErrorOf(entry.File);
            }
        }

        /*
         * "Game: Connected" answers the question the old window could not: is any
         * of this reaching the game at all?
         *
         * Two facts, never one: the game process, and whether Crane's report
         * belongs to the run of it that is up now. Without the second, a broken
         * script and a game that is not running look the same.
         *
         * This comment said the answer was "inferred from the status file's age,
         * because Crane rewrites it on every reload". Crane does not: it writes
         * the file from reload_scripts and nowhere else, so an untouched session
         * writes it once. See StatusFile.Judge.
         */
        private void UpdateConnection()
        {
            /*
             * Two facts, reported as three states, because conflating them is
             * what made this wrong twice.
             *
             *   is the game running  -- the process. Ground truth.
             *   has Crane reported   -- its report against the process start.
             *
             * "Connected" was previously the second fact alone, expressed as
             * file age, so it went on claiming a connection for up to five
             * minutes after the game had exited: a report written five seconds
             * ago and a game that died four minutes ago are indistinguishable by
             * age alone. Asking about the process makes the answer immediate and
             * exact -- and once the process is being asked anyway, its start
             * time replaces the age window outright.
             *
             * The middle state earns its own wording rather than being folded
             * into either neighbour, because between clicking Launch and Crane's
             * first report there are several seconds in which neither "not
             * running" nor "connected" is true.
             */
            GameLaunch.GameProcess game = GameLaunch.Find();
            bool gameUp = game.Running;
            StatusFile.SessionVerdict verdict = StatusFile.Judge(_folder, gameUp, game.StartedUtc);
            bool reporting = verdict.Current;
            /* Three states, one vocabulary, and the same words in the capsule
               and the status bar. */
            ConnectionText.Text = reporting ? "GAME ATTACHED"
                                : gameUp ? "GAME STARTING"
                                : "GAME OFFLINE";
            bool running = reporting;
            /*
             * The capsule says why, not just what.
             *
             * It used to report the timestamp alone -- true, and useless for the
             * question anyone actually hovers it to ask, which is "the game is
             * plainly running, so why does this not say so". Both times this
             * indicator has been wrong, the evidence available to the user was
             * two words on a screenshot, and working back to the cause took a
             * round trip. The reason comes from the same call that decides the
             * verdict, so it cannot describe a rule that is no longer in force.
             *
             * On the capsule rather than the text: it is the whole hover target,
             * dot included, and the dot is the part people point at.
             */
            ConnectionCapsule.ToolTip = verdict.Reason;

            // Qualified: this file uses System.IO.Path, so importing
            // System.Windows.Shapes would make every Path.Combine ambiguous.
            ConnectionDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,
                reporting ? "StateGood" : gameUp ? "StateWarn" : "StateIdle");
            ConnectionText.SetResourceReference(TextBlock.ForegroundProperty,
                running ? "Ink" : "InkMuted");
            // The capsule's own border carries the state too, so the readout is
            // legible from the shape of it before any word is read.
            ConnectionCapsule.SetResourceReference(Border.BorderBrushProperty,
                running ? "AccentDim" : "Rule");

            /*
             * Persistent instrumentation, right-aligned, distinct from the
             * transient prose on the left. Counts plus the same state word.
             *
             * Counted from the rows, not from _entries. It counted _entries and
             * so disagreed with the left-hand end of the same status bar --
             * "10 scripts" beside "14 SCRIPTS", in one screenshot, four of them
             * manifest entries whose file is not in \scripts and which are
             * therefore not shown. UpdateSummary already carries the note about
             * why the visible rows are the honest number; this end of the bar
             * was still using the other one.
             */
            int total, on;
            CountScripts(out total, out on);
            StatusRight.Text = string.Format(CultureInfo.InvariantCulture,
                "{0} SCRIPT{1}  •  {2} ENABLED  •  {3}",
                total, total == 1 ? "" : "S", on, ConnectionText.Text);
        }

        /*
         * The conflict block: who is taking what, and the one button that
         * changes it.
         *
         * Placed above the description and the settings for the same reason the
         * load failure is: somebody whose script is being refused its levers has
         * no use for a list of knobs that will not be read.
         *
         * The paths are named in full rather than counted. They are Bridge paths
         * and they mean something to the kind of person who installed two
         * scripts that fight -- and when the overlap is partial, which six of
         * eight is the whole question.
         */
        private void ShowConflict(ScriptEntry entry)
        {
            ScriptConflict conflict = entry.Conflict;
            if (!entry.Enabled) return;
            if (!conflict.Refused && conflict.Won.Count == 0) return;

            Border block = new Border();
            block.SetResourceReference(Border.BackgroundProperty, "SurfaceAlert");
            block.SetResourceReference(Border.BorderBrushProperty,
                conflict.Refused && !conflict.Covered ? "StateWarn" : "Rule");
            block.BorderThickness = new Thickness(3, 0, 0, 0);
            block.Padding = new Thickness(10, 8, 10, 8);
            block.Margin = new Thickness(0, 12, 0, 0);

            StackPanel body = new StackPanel();

            TextBlock headline = new TextBlock();
            headline.TextWrapping = TextWrapping.Wrap;
            headline.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            if (conflict.Silenced)
                headline.Text = "This script will do nothing. " + conflict.Rival +
                                " loads first and holds everything it asks for.";
            else if (conflict.Covered)
                headline.Text = conflict.Rival + " loads first and holds what this " +
                                "script asks for, and this script " + conflict.Fallback +
                                ". Nothing is lost, but only one of them is driving " +
                                "these settings directly.";
            else if (conflict.Refused)
                headline.Text = string.Format(CultureInfo.InvariantCulture,
                    "Partly overridden. {0} loads first and holds {1} of the {2} " +
                    "settings this script asks for.",
                    conflict.Rival, conflict.Lost.Count, conflict.Wanted);
            else
                headline.Text = "This script loads first and holds settings that " +
                                OtherSide(conflict.Won) + " also asks for.";
            body.Children.Add(headline);

            TextBlock paths = new TextBlock();
            paths.Text = PathList(conflict.Refused ? conflict.Lost : conflict.Won);
            paths.Style = (Style)FindResource("Subtle");
            paths.TextWrapping = TextWrapping.Wrap;
            paths.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(paths);

            /*
             * Only the losing side gets a button, and it does the one thing that
             * resolves the standoff without asking the user to hold the load
             * order in their head: put this script directly above the script
             * beating it. The alternative -- turn one of them off -- is the
             * checkbox they already have.
             *
             * It survives the list going back to load order. Dragging and the
             * two buttons both ask the user to work out where "above the one
             * beating me" is and then get there; this states the outcome and
             * lets the manager do the arithmetic, which is still the shortest
             * route from seeing the amber dot to resolving it.
             */
            if (conflict.Refused && conflict.RivalFile.Length > 0)
            {
                Button promote = new Button();
                promote.Content = "Run this one instead";
                promote.Padding = new Thickness(12, 4, 12, 4);
                promote.HorizontalAlignment = HorizontalAlignment.Left;
                promote.Margin = new Thickness(0, 10, 0, 0);
                promote.ToolTip = "Moves this script above " + conflict.Rival +
                                  " in the load order, so it claims first. " +
                                  conflict.Rival + " is then the one refused.";
                string rivalFile = conflict.RivalFile;
                promote.Click += delegate { PromoteAbove(entry, rivalFile); };
                body.Children.Add(promote);
            }

            block.Child = body;
            Detail.Children.Add(block);
        }

        // "and Steady Hands" / "and 3 other scripts". Naming every loser in a
        // headline is unreadable past two, and the losers each carry the full
        // statement on their own row anyway.
        private static string OtherSide(List<ClaimClash> clashes)
        {
            List<string> names = new List<string>();
            foreach (ClaimClash clash in clashes)
                if (!names.Contains(clash.OtherName)) names.Add(clash.OtherName);
            if (names.Count == 1) return names[0];
            if (names.Count == 2) return names[0] + " and " + names[1];
            return string.Format(CultureInfo.InvariantCulture, "{0} and {1} other scripts",
                                 names[0], names.Count - 1);
        }

        private static string PathList(List<ClaimClash> clashes)
        {
            string text = "";
            List<string> seen = new List<string>();
            foreach (ClaimClash clash in clashes)
            {
                if (seen.Contains(clash.Path)) continue;
                seen.Add(clash.Path);
                text += (text.Length == 0 ? "" : "\n") + clash.Path;
            }
            return text;
        }

        /*
         * Moves an entry to sit immediately above the entry beating it.
         *
         * Directly above rather than to the top of the manifest: the user asked
         * to beat one script, and hoisting past every other script would also
         * silently take paths off a third one that had nothing to do with it.
         */
        private void PromoteAbove(ScriptEntry entry, string rivalFile)
        {
            int from = _entries.IndexOf(entry);
            int to = -1;
            for (int i = 0; i < _entries.Count; i++)
                if (string.Equals(_entries[i].File, rivalFile, StringComparison.OrdinalIgnoreCase))
                { to = i; break; }
            if (from < 0 || to < 0 || to >= from) return;

            _entries.RemoveAt(from);
            _entries.Insert(to, entry);
            Save();
            RefreshConflicts(true);
            UpdateSummary();
            StatusText.Text = entry.Name + " now loads first.";
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
            // Metadata, not an inactive control. InkFaint is the disabled level
            // now, and spending it here would blur the two categories again.
            file.SetResourceReference(TextBlock.ForegroundProperty, "InkMuted");
            file.Margin = new Thickness(0, 1, 0, 0);
            Detail.Children.Add(file);

            // A failure goes above everything. Somebody whose script just failed
            // does not want to tune it.
            if (entry.State == ScriptState.Failed && entry.StateError.Length > 0)
            {
                Border alert = new Border();
                alert.SetResourceReference(Border.BackgroundProperty, "SurfaceAlert");
                // A bar on the leading edge, the same idiom the selected row
                // uses, rather than a box outline. The colour marks the block;
                // the text stays at full contrast.
                alert.SetResourceReference(Border.BorderBrushProperty, "StateBad");
                alert.BorderThickness = new Thickness(3, 0, 0, 0);
                alert.Padding = new Thickness(10, 8, 10, 8);
                alert.Margin = new Thickness(0, 12, 0, 0);
                TextBlock text = new TextBlock();
                text.Text = entry.StateError;
                text.TextWrapping = TextWrapping.Wrap;
                text.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
                alert.Child = text;
                Detail.Children.Add(alert);
            }

            ShowConflict(entry);

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
             * cannot recover.
             *
             * The script list is the same question answered the same way: its
             * order is the load order the user chose, and sorting it threw away
             * a fact only they could put back.
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
                heading.Text = (group.Length == 0 ? "Settings" : group).ToUpperInvariant();
                heading.Style = (Style)FindResource("SectionHeading");
                Detail.Children.Add(heading);
                Detail.Children.Add(SectionRule());

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
             * Load order, which is no longer list order.
             *
             * The WinForms window had Move up and Move down; the WPF port
             * silently dropped them, and nothing noticed because order is
             * invisible until two scripts want the same thing. They came back,
             * the list went alphabetical for one release, and now it is the load
             * order again -- so the row does move when these are pressed.
             *
             * The wording stays "Load earlier" / "Load later" rather than
             * reverting to "Move up". Naming the effect outlasted the list
             * arrangement that made "up" true, and it will still be true if the
             * list is ever grouped or filtered; "up" would quietly become a lie
             * again. These are also the keyboard route to something that is
             * otherwise a mouse gesture, so they are not decoration.
             *
             * Leases are exclusive per path, so when two enabled scripts claim
             * one path the earlier-loading entry wins and the other is refused.
             * For the case that motivates all of this -- two scripts already
             * fighting -- the conflict block above is the direct route and these
             * are the general one.
             */
            /*
             * Bounded by the enabled run, exactly as the drag is. These move the
             * same script through the same list, and a button that can push a
             * row somewhere a drag refuses to put it would make the arrangement
             * depend on which control the user reached for.
             */
            int at = _entries.IndexOf(entry);
            int boundary = LoadOrder.Boundary(_entries, null);
            bool orderable = at >= 0 && entry.Enabled && !row.Unlisted;

            Button up = new Button();
            up.Content = "Load earlier";
            up.Padding = new Thickness(12, 4, 12, 4);
            up.IsEnabled = orderable && at > 0;
            up.Click += delegate { MoveSelected(-1); };

            Button down = new Button();
            down.Content = "Load later";
            down.Padding = new Thickness(12, 4, 12, 4);
            down.Margin = new Thickness(8, 0, 0, 0);
            down.IsEnabled = orderable && at < boundary - 1;
            down.Click += delegate { MoveSelected(1); };

            StackPanel order = new StackPanel();
            order.Orientation = Orientation.Horizontal;
            order.HorizontalAlignment = HorizontalAlignment.Left;
            order.Margin = new Thickness(0, 16, 0, 0);
            order.Children.Add(up);
            order.Children.Add(down);
            Detail.Children.Add(order);

            /*
             * No "Loads 4 of 9" line here any more (user, 2026-08-21).
             *
             * It existed to state a position the list could not show, back when
             * the list was alphabetical. The list is the load order again, so the
             * row's own place in it says the same thing better, and the line was
             * restating the screen.
             *
             * It was also wrong, and wrong in the way that made the redundancy
             * worth removing rather than repairing: it printed "Loads 8 of 6",
             * because the position was an index into the manifest and the total
             * was a count of enabled entries. Those agree only while the enabled
             * run starts at the top, and a manifest carried over from an earlier
             * version had two disabled scripts above it. The index arithmetic is
             * fixed either way -- see LoadOrder.EnabledCount -- but a number that
             * duplicates what the reader can already see is a number that can
             * only be right or embarrassing, and never useful.
             *
             * The unlisted and disabled cases keep a line, because those DO say
             * something the list does not: what ticking the script will do.
             */
            if (row.Unlisted)
                Detail.Children.Add(Muted(
                    "Not in the load order yet. Ticking this script puts it last " +
                    "among the enabled ones, where it claims after all of them. " +
                    "Order decides who wins: if two enabled scripts claim the same " +
                    "thing, the one that loads first gets it and the other is refused."));
            else if (!entry.Enabled)
                Detail.Children.Add(Muted(
                    "Switched off, so it has no place in the load order and claims " +
                    "nothing. Ticking it puts it last among the enabled scripts, " +
                    "where it claims after every one of them."));

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
            // Metadata, not an inactive control. InkFaint is the disabled level
            // now, and spending it here would blur the two categories again.
            file.SetResourceReference(TextBlock.ForegroundProperty, "InkMuted");
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
                heading.Text = (group.Length == 0 ? "Settings" : group).ToUpperInvariant();
                heading.Style = (Style)FindResource("SectionHeading");
                target.Children.Add(heading);
                target.Children.Add(SectionRule());

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
            grid.Margin = new Thickness(SettingIndent, 0, 0, 10);
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
                note.SetResourceReference(TextBlock.ForegroundProperty,
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
            grid.Margin = new Thickness(SettingIndent, 0, 0, 10);
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
                // This is the description line itself, so it belongs on the
                // secondary level, not the disabled one.
                note.SetResourceReference(TextBlock.ForegroundProperty, "InkMuted");
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
            // A parameter can be the gate on a claim -- Steady Hands stops
            // wanting interaction.Container.duration_scale the moment its
            // Containers tick comes off -- so the warning has to follow the
            // setting rather than wait for the next reload.
            RefreshConflicts(false);
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
        // A script means a row: a file in \scripts. Both ends of the status bar
        // count through here so they cannot disagree again.
        private void CountScripts(out int total, out int enabled)
        {
            total = 0;
            enabled = 0;
            System.Collections.IEnumerable rows = ScriptList.ItemsSource as System.Collections.IEnumerable;
            if (rows == null) return;
            foreach (object item in rows)
            {
                ScriptRow row = item as ScriptRow;
                if (row == null) continue;
                total++;
                if (row.Enabled) enabled++;
            }
        }

        private void UpdateSummary()
        {
            int total, enabled;
            CountScripts(out total, out enabled);
            int failed = 0, reported = 0;
            System.Collections.IEnumerable rows = ScriptList.ItemsSource as System.Collections.IEnumerable;
            if (rows != null)
                foreach (object item in rows)
                {
                    ScriptRow row = item as ScriptRow;
                    if (row == null) continue;
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
