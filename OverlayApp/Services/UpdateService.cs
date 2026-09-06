using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

namespace OverlayApp.Services
{
    /// <summary>
    /// Checks for app updates from the remote version endpoint and performs an in-place update
    /// by downloading the new executable, swapping it via a helper batch script, and restarting.
    /// </summary>
    public class UpdateService
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        public record UpdateInfo(bool UpdateAvailable, string LatestVersion, string DownloadUrl, string ReleaseNotes);

        /// <summary>Current hardcoded app version — bump this on every release.</summary>
        public const string CurrentVersion = "2.0.0";

        /// <summary>
        /// Checks the remote version endpoint and returns update info.
        /// Returns null on network failure (silent fail).
        /// </summary>
        public static async Task<UpdateInfo?> CheckForUpdateAsync(string apiBaseUrl)
        {
            try
            {
                string url = $"{apiBaseUrl.TrimEnd('/')}/api/version";
                string json = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string latest = root.GetProperty("version").GetString() ?? "";
                string download = root.GetProperty("downloadUrl").GetString() ?? "";
                string notes = root.TryGetProperty("releaseNotes", out var rn) ? rn.GetString() ?? "" : "";

                bool newer = IsNewerVersion(latest, CurrentVersion);
                return new UpdateInfo(newer, latest, download, notes);
            }
            catch
            {
                return null; // silent fail — no network, endpoint down, etc.
            }
        }

        /// <summary>
        /// Downloads the new exe and performs an in-place swap via a batch script, then restarts.
        /// Reports progress via the callback (0.0 – 1.0).
        /// </summary>
        public static async Task DownloadAndInstallAsync(string downloadUrl, Action<double> onProgress)
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                             ?? Path.Combine(AppContext.BaseDirectory, "SystemCoreHost.exe");
            string dir = Path.GetDirectoryName(exePath)!;
            string newExePath = Path.Combine(dir, "SystemCoreHost_update.exe");
            string batchPath = Path.Combine(dir, "_shadow_update.bat");

            // Download with progress
            using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? -1;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var file = File.Create(newExePath);

            byte[] buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                if (total > 0) onProgress((double)downloaded / total);
            }
            file.Close();
            onProgress(1.0);

            // Write a self-deleting batch script that:
            // 1. Waits for this process to exit
            // 2. Replaces the exe
            // 3. Starts the new exe
            // 4. Deletes itself
            string batch = $@"@echo off
:waitloop
tasklist /FI ""IMAGENAME eq SystemCoreHost.exe"" 2>NUL | find /I ""SystemCoreHost.exe"" >NUL
if not errorlevel 1 (
    timeout /t 1 /nobreak >NUL
    goto waitloop
)
move /Y ""{newExePath}"" ""{exePath}""
start """" ""{exePath}""
del ""%~f0""
";
            await File.WriteAllTextAsync(batchPath, batch);

            // Launch batch and exit current process
            Process.Start(new ProcessStartInfo
            {
                FileName = batchPath,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = true
            });

            // Shut down the current instance
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                System.Windows.Application.Current.Shutdown());
        }

        private static bool IsNewerVersion(string remote, string local)
        {
            if (Version.TryParse(remote, out var r) && Version.TryParse(local, out var l))
                return r > l;
            return string.Compare(remote, local, StringComparison.Ordinal) > 0;
        }
    }
}
