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

                System.Threading.Thread.Sleep(800);

                // Remove shortcuts first (outside install folder — no lock issues)
                string desktopShortcut   = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Shadow AI.lnk");
                string startMenuShortcut = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Shadow AI.lnk");

                try { if (File.Exists(desktopShortcut))   File.Delete(desktopShortcut); } catch { }
                try { if (File.Exists(startMenuShortcut)) File.Delete(startMenuShortcut); } catch { }

                // Remove registry entries before deleting folder
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ShadowAI",   throwOnMissingSubKey: false); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SystemCore", throwOnMissingSubKey: false); } catch { }

                // We cannot delete the install folder while uninstall.exe (this process) is running inside it.
                // Strategy: copy ourselves to %TEMP%, then launch a batch from there that:
                //   1. Waits for this process to exit
                //   2. Deletes the install folder
                //   3. Deletes the batch file itself
                string tempDir     = Path.Combine(Path.GetTempPath(), "ShadowAIUninstall_" + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(tempDir);

                string tempBatch = Path.Combine(tempDir, "cleanup.bat");
                string batchScript = $@"@echo off
:wait
tasklist /FI ""PID eq {Process.GetCurrentProcess().Id}"" 2>NUL | find ""{Process.GetCurrentProcess().Id}"" >NUL
if not errorlevel 1 (
    timeout /t 1 /nobreak >NUL
    goto wait
)
if exist ""{installFolder}"" rmdir /s /q ""{installFolder}""
rmdir /s /q ""{tempDir}"" >NUL 2>&1
del ""%~f0"" >NUL 2>&1
";
                File.WriteAllText(tempBatch, batchScript);

                Process.Start(new ProcessStartInfo
                {
                    FileName        = tempBatch,
                    CreateNoWindow  = true,
                    WindowStyle     = ProcessWindowStyle.Hidden,
                    UseShellExecute = true
                });

                MessageBox.Show("Shadow AI has been successfully uninstalled.", "Shadow AI Uninstaller", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Uninstall failed: {ex.Message}", "Shadow AI Uninstaller", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
