using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace OverlayInstaller
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private async void InstallButton_Click(object sender, RoutedEventArgs e)
        {
            // Switch screen to progress mode
            InstallScreen.Visibility = Visibility.Collapsed;
            ProgressScreen.Visibility = Visibility.Visible;

            try
            {
                // Step 1: Find embedded zip payload
                UpdateProgress("Checking installation assets...", 10);
                await Task.Delay(200);

                var assembly = Assembly.GetExecutingAssembly();
                string zipResource = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("AppFiles.zip", StringComparison.OrdinalIgnoreCase))
                    ?? throw new FileNotFoundException("Application payload (AppFiles.zip) not found inside installer package.");

                // Step 2: Establish target folder
                UpdateProgress("Creating installation folder...", 20);
                await Task.Delay(200);

                string appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string installFolder = Path.Combine(appDataLocal, "SystemCore");
                Directory.CreateDirectory(installFolder);

                string targetExePath = Path.Combine(installFolder, "SystemCoreHost.exe");
                if (!File.Exists(targetExePath))
                {
                    string altExe = Path.Combine(installFolder, "SystemCore.exe");
                    if (File.Exists(altExe)) targetExePath = altExe;
                }

                // Step 2b: Kill any running instance of the app before overwriting
                UpdateProgress("Stopping running instances...", 25);
                await Task.Delay(200);
                try
                {
                    foreach (var procName in new[] { "SystemCoreHost", "SystemCore" })
                    {
                        foreach (var proc in System.Diagnostics.Process.GetProcessesByName(procName))
                        {
                            proc.Kill();
                            await Task.Run(() => proc.WaitForExit(3000));
                        }
                    }
                    await Task.Delay(500);
                }
                catch { /* ignore */ }

                // Step 3: Extract zip archive
                using (Stream? zipStream = assembly.GetManifestResourceStream(zipResource))
                {
                    if (zipStream == null) throw new InvalidOperationException("Could not read embedded installer payload.");
                    using var archive = new System.IO.Compression.ZipArchive(zipStream, System.IO.Compression.ZipArchiveMode.Read);
                    int total = archive.Entries.Count;
                    for (int i = 0; i < total; i++)
                    {
                        var entry = archive.Entries[i];
                        if (string.IsNullOrEmpty(entry.Name)) continue; // skip directory entries

                        string destPath = Path.Combine(installFolder, entry.FullName);
                        string? destDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                        entry.ExtractToFile(destPath, overwrite: true);
                        double pct = 25 + (i + 1) * 50.0 / total;
                        UpdateProgress($"Extracting {entry.Name}...", pct);
                    }
                }

                // Determine final executable path
                if (File.Exists(Path.Combine(installFolder, "SystemCore.exe")))
                    targetExePath = Path.Combine(installFolder, "SystemCore.exe");
                else if (File.Exists(Path.Combine(installFolder, "SystemCoreHost.exe")))
                    targetExePath = Path.Combine(installFolder, "SystemCoreHost.exe");

                // Step 4: Create shortcuts
                UpdateProgress("Creating desktop shortcut...", 75);
                await Task.Delay(300);

                string desktopFolder   = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string desktopShortcut = Path.Combine(desktopFolder, "Shadow AI.lnk");
                string startMenuFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                string startMenuShortcut = Path.Combine(startMenuFolder, "Shadow AI.lnk");

                CreateShortcut(desktopShortcut, targetExePath);
                CreateShortcut(startMenuShortcut, targetExePath);

                // Step 5: Register in Apps & Features
                UpdateProgress("Registering application...", 90);
                await Task.Delay(300);

                // Clean up any old registry entry from previous installs
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\SystemCore", false); } catch { }

                RegisterInSettingsApps(installFolder, targetExePath, desktopShortcut, startMenuShortcut);

                UpdateProgress("Installation complete!", 100);
                await Task.Delay(400);

                ProgressScreen.Visibility = Visibility.Collapsed;
                FinishedScreen.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Installation failed:\n{ex.Message}", "Shadow AI Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ProgressScreen.Visibility = Visibility.Collapsed;
                InstallScreen.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// The .NET embedded resource system replaces '.' in filenames with '.' too,
        /// so compound extensions like ".deps.json" or ".runtimeconfig.json" get mangled.
        /// This restores the known filenames.
        /// </summary>
        private static string RestoreFileName(string resourceSegment)
        {
            // Known multi-dot filenames
            if (resourceSegment.EndsWith("deps.json",            StringComparison.OrdinalIgnoreCase)) return "SystemCoreHost.deps.json";
            if (resourceSegment.EndsWith("runtimeconfig.json",   StringComparison.OrdinalIgnoreCase)) return "SystemCoreHost.runtimeconfig.json";
            // Simple: last segment after the final dot-sequence that forms an extension
            // For DLLs / EXEs the resource name is already correct ("SystemCoreHost.exe" → "SystemCoreHost.exe")
            return resourceSegment;
        }

        private void UpdateProgress(string status, double percent)
        {
            StatusText.Text = status;
            InstallProgressBar.Value = percent;
        }

        private void CreateShortcut(string shortcutPath, string targetPath)
        {
            try
            {
                // Escape single quotes for PowerShell commands safely
                string escShortcut = shortcutPath.Replace("'", "''");
                string escTarget = targetPath.Replace("'", "''");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"$s = (New-Object -ComObject WScript.Shell).CreateShortcut('{escShortcut}'); $s.TargetPath = '{escTarget}'; $s.Save()\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (var process = Process.Start(startInfo))
                {
                    process?.WaitForExit();
                }
            }
            catch (Exception)
            {
                // Mute errors
            }
        }

        private void RegisterInSettingsApps(string installFolder, string targetExePath, string desktopShortcut, string startMenuShortcut)
        {
            try
            {
                string regPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ShadowAI";

                // Copy the installer exe itself into the install folder so uninstall works even if setup.exe is deleted
                string uninstallerExe = Path.Combine(installFolder, "uninstall.exe");
                string currentExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                // GetLocation returns .dll in single-file; use process path instead
                string processExe = Process.GetCurrentProcess().MainModule?.FileName ?? currentExe;
                if (File.Exists(processExe) && !string.Equals(processExe, uninstallerExe, StringComparison.OrdinalIgnoreCase))
                    File.Copy(processExe, uninstallerExe, overwrite: true);

                using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(regPath))
                {
                    if (key == null) return;

                    key.SetValue("DisplayName",     "Shadow AI");
                    key.SetValue("DisplayIcon",     targetExePath + ",0");
                    key.SetValue("Publisher",       "Shadow AI");
                    key.SetValue("DisplayVersion",  "8.5.0");
                    key.SetValue("InstallLocation", installFolder);
                    key.SetValue("EstimatedSize",   26000);
                    key.SetValue("NoModify",        1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair",        1, RegistryValueKind.DWord);
                    // UninstallString points to the copied exe with --uninstall flag
                    key.SetValue("UninstallString", $"\"{uninstallerExe}\" --uninstall");
                }
            }
            catch (Exception)
            {
                // Mute registry errors
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (FinishedScreen.Visibility == Visibility.Visible && LaunchCheckBox.IsChecked == true)
            {
                try
                {
                    string appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    string targetExePath = Path.Combine(appDataLocal, "SystemCore", "SystemCoreHost.exe");
                    if (File.Exists(targetExePath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = targetExePath,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception)
                {
                    // Mute launch errors
                }
            }
            base.OnClosed(e);
        }
    }
}
