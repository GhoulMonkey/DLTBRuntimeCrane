// SPDX-License-Identifier: GPL-3.0-only
/*
 * Application entry point, and a startup self-test.
 *
 * dev.21 shipped an exe that died on launch: MainWindow.xaml referenced
 * Icon="CraneLoader.ico" while the icon existed only as a Win32 resource, so
 * InitializeComponent threw a pack-URI failure and the process exited before
 * drawing anything. 167 unit tests passed, because not one of them constructed
 * the window -- the logic layer was fully covered and the thing the user
 * actually double-clicks was not covered at all. The icon was only what
 * exposed the gap.
 *
 * --selftest constructs MainWindow and exits without showing it. Constructing it
 * is what matters: InitializeComponent resolves every resource URI, style key
 * and converter reference in the XAML, so "compiles, then dies on launch"
 * becomes a build failure instead of a user report.
 */

using System;
using System.Collections.Generic;
using System.Windows;

namespace CraneLoader
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Before any window is constructed, so the first frame is already the
            // chosen palette rather than a flash of the default one.
            Theme.Apply(Settings.LoadTheme());

            bool selftest = false;
            foreach (string argument in e.Args)
                if (string.Equals(argument, "--selftest", StringComparison.OrdinalIgnoreCase))
                    selftest = true;

            if (!selftest)
            {
                // The normal path. Explicit rather than StartupUri, because
                // StartupUri would also fire under --selftest and put the real
                // window on screen during a build.
                new MainWindow().Show();
                return;
            }

            // Nothing is shown, so there is no last window to close and the
            // default shutdown mode would hang instead of reporting.
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                MainWindow window = new MainWindow();
                if (window.Content == null)
                    throw new InvalidOperationException("MainWindow has no content");
                Console.Out.WriteLine("selftest: MainWindow constructed");

                /* Every theme, not just the saved one. A palette is a table of
                   strings matched against resource keys, and neither the
                   compiler nor the tests check that the two agree. Applying all
                   of them here turns a mistyped key or colour into a build
                   failure. */
                foreach (ThemeDefinition definition in Theme.All)
                {
                    Theme.Apply(definition.Name);
                    if (!string.Equals(Theme.Current, definition.Name,
                                       StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            "theme did not apply: " + definition.Name);
                    foreach (KeyValuePair<string, string> entry in definition.Colors)
                    {
                        System.Windows.Media.SolidColorBrush brush =
                            Current.Resources[entry.Key] as System.Windows.Media.SolidColorBrush;
                        if (brush == null)
                            throw new InvalidOperationException(
                                definition.Name + " sets '" + entry.Key +
                                "', which is not a SolidColorBrush in App.xaml");
                        // Theme.Apply skips a frozen brush silently, so the theme
                        // would half-apply with nothing logged. Compare the
                        // colour rather than assuming it took.
                        System.Windows.Media.Color want =
                            (System.Windows.Media.Color)
                            System.Windows.Media.ColorConverter.ConvertFromString(entry.Value);
                        if (brush.Color != want)
                            throw new InvalidOperationException(
                                definition.Name + ": '" + entry.Key +
                                "' did not take the theme colour");
                    }
                }
                Console.Out.WriteLine("selftest: " + Theme.All.Length +
                                      " themes applied");
                Theme.Apply(Settings.LoadTheme());
                Shutdown(0);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("selftest FAILED: " + error.GetType().Name +
                                        ": " + error.Message);
                Shutdown(1);
            }
        }
    }
}
