using System;
using System.Threading;
using System.Windows;

namespace ProjectorDash
{
    public static class Program
    {
        /// <summary>
        /// Set by the UI (e.g. "Reassign displays") to relaunch the app after
        /// the single-instance mutex has been released.
        /// </summary>
        public static bool RestartRequested;

        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Length > 0 &&
                string.Equals(args[0], "--apply-update",
                    StringComparison.OrdinalIgnoreCase))
            {
                SelfUpdater.RunApplyMode(args);
                return;
            }

            string updateReadyMarker = "";
            if (args != null && args.Length >= 2 &&
                string.Equals(args[0], "--update-ready",
                    StringComparison.OrdinalIgnoreCase))
                updateReadyMarker = args[1];

            bool createdNew;
            // Single instance: a bedside appliance must never stack copies of itself.
            using (Mutex mutex = new Mutex(true, "ProjectorDashboard_SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                AppConfig cfg = AppConfig.Load();

                // First run (or a saved display no longer exists): ask which screen is which.
                if (!cfg.DisplaysConfigured())
                {
                    DisplayPickerWindow picker = new DisplayPickerWindow(cfg);
                    bool? ok = picker.ShowDialog();
                    if (ok != true)
                    {
                        return; // user cancelled setup
                    }
                    cfg.Save();
                }

                ControllerWindow controller = new ControllerWindow(cfg);
                controller.Show();
                SelfUpdater.SignalUpdatedBuildReady(updateReadyMarker);

                app.Run();
            }

            // Mutex is released here, so a fresh instance can start cleanly.
            if (RestartRequested)
            {
                try
                {
                    System.Diagnostics.Process.Start(
                        System.Reflection.Assembly.GetExecutingAssembly().Location);
                }
                catch { }
            }
        }
    }
}
