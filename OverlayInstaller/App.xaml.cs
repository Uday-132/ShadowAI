using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace OverlayInstaller
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // If launched with --uninstall, run silently and exit
            if (e.Args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                RunUninstall();
                Shutdown(0);
                return;
            }

            // Normal install UI
            new MainWindow().Show();
        }

        private static void RunUninstall()
        {
            try
            {
                string appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string installFolder = Path.Combine(appDataLocal, "SystemCore");

                // Kill the running app first
                try
                {
                    foreach (var proc in Process.GetProcessesByName("SystemCoreHost"))
                    {
                        proc.Kill();
                        proc.WaitForExit(5000);
                    }
                }
                catch { }

                System.Threading.Thread.Sleep(1000);

                // Remove install folder
                if (Directory.Exists(installFolder))
                    Directory.Delete(installFolder, recursive: true);

                // Remove shortcuts
                string desktopShortcut   = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Shadow AI.lnk");
                string startMenuShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Shadow AI.lnk");

                if (File.Exists(desktopShortcut))   File.Delete(desktopShortcut);
                if (File.Exists(startMenuShortcut)) File.Delete(startMenuShortcut);

                // Remove registry entries (both old and new key names)
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ShadowAI",   throwOnMissingSubKey: false);
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SystemCore", throwOnMissingSubKey: false);

                MessageBox.Show("Shadow AI has been successfully uninstalled.", "Shadow AI Uninstaller", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Uninstall failed: {ex.Message}", "Shadow AI Uninstaller", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
