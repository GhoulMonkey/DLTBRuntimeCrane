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
                new MainWindow().Show();
                return;
            }

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
