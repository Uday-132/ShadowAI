using System;
using System.Diagnostics;
using System.IO;
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
                // Step 1: Query embedded resource
                UpdateProgress("Checking installation assets...", 15);
                await Task.Delay(400);

                var assembly = Assembly.GetExecutingAssembly();
                string resourceName = "";
                foreach (string name in assembly.GetManifestResourceNames())
                {
                    if (name.EndsWith("SystemCoreHost.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        resourceName = name;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(resourceName))
                {
                    throw new FileNotFoundException("System core host assets not found inside installer package.");
                }

                // Step 2: Establish target paths
                UpdateProgress("Creating target folders...", 30);
                await Task.Delay(400);

                string appDataLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string installFolder = Path.Combine(appDataLocal, "SystemCore");
                if (!Directory.Exists(installFolder))
                {
                    Directory.CreateDirectory(installFolder);
                }
                string targetExePath = Path.Combine(installFolder, "SystemCoreHost.exe");

                // Step 3: Extract binary file
                UpdateProgress("Extracting application components...", 50);
                await Task.Delay(400);

                using (Stream? input = assembly.GetManifestResourceStream(resourceName))
                {
                    if (input == null) throw new InvalidOperationException("Could not open embedded assembly stream.");
                    using (FileStream output = new FileStream(targetExePath, FileMode.Create, FileAccess.Write))
                    {
                        await input.CopyToAsync(output);
                    }
                }

                // Step 4: Create shortcuts
                UpdateProgress("Generating desktop and start menu shortcuts...", 75);
                await Task.Delay(400);

                string desktopFolder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string desktopShortcut = Path.Combine(desktopFolder, "Overlay HUD.lnk");

                string startMenuFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs");
                string startMenuShortcut = Path.Combine(startMenuFolder, "Overlay HUD.lnk");

                CreateShortcut(desktopShortcut, targetExePath);
                CreateShortcut(startMenuShortcut, targetExePath);

                // Step 5: Configure Windows App list registry
                UpdateProgress("Registering application configuration...", 90);
                await Task.Delay(400);

                RegisterInSettingsApps(installFolder, targetExePath, desktopShortcut, startMenuShortcut);

                UpdateProgress("Installation successful!", 100);
                await Task.Delay(400);

                // Switch to complete slide
                ProgressScreen.Visibility = Visibility.Collapsed;
                FinishedScreen.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Installation failed:\n{ex.Message}", "Overlay HUD Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Roll back to install screen
                ProgressScreen.Visibility = Visibility.Collapsed;
                InstallScreen.Visibility = Visibility.Visible;
            }
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
                string regPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SystemCore";
                using (RegistryKey? key = Registry.CurrentUser.CreateSubKey(regPath))
                {
                    if (key == null) return;

                    key.SetValue("DisplayName", "Overlay HUD");
                    key.SetValue("DisplayIcon", targetExePath);
                    key.SetValue("Publisher", "Overlay HUD Project");
                    key.SetValue("DisplayVersion", "1.0.0");
                    key.SetValue("EstimatedSize", 250); // Approximated size in KB

                    // The uninstall string executes command to remove folder, shortcuts, and delete this registry key
                    string cleanFolderCmd = $"cmd.exe /c \"rmdir /s /q \\\"{installFolder}\\\" & del \\\"{desktopShortcut}\\\" & del \\\"{startMenuShortcut}\\\" & reg delete \\\"HKCU\\{regPath}\\\" /f\"";
                    key.SetValue("UninstallString", cleanFolderCmd);
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
