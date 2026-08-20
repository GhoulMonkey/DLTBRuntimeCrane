// SPDX-License-Identifier: GPL-3.0-only
/*
 * Application entry point, and a startup self-test.
 *
 * dev.21 shipped an exe that died on launch: MainWindow.xaml referenced
 * Icon="CraneManager.ico" while the icon existed only as a Win32 resource, so
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
using System.Windows;

namespace CraneManager
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

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
