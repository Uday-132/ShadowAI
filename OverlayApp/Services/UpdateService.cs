using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

namespace OverlayApp.Services
{
    /// <summary>
    /// Handles silent background updates for Shadow AI.
    ///
    /// Flow:
    ///   1. CheckForUpdateAsync()  — polls /api/version, returns whether a newer version exists.
    ///   2. DownloadUpdateAsync()  — downloads the new SystemCoreHost.exe to a temp file.
    ///                               App keeps running normally. Returns path to staged file.
    ///   3. ApplyUpdateAndRestart() — only called when user explicitly clicks "Restart to Apply".
    ///                               Writes a batch that swaps the exe after this process exits,
    ///                               then cleanly shuts down. If anything failed before this point
    ///                               the current exe is never touched.
    /// </summary>
    public class UpdateService
    {
        private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        public record UpdateInfo(bool UpdateAvailable, string LatestVersion, string DownloadUrl, string ReleaseNotes);

        /// <summary>Current hardcoded app version — bump this on every release.</summary>
        public const string CurrentVersion = "7.0.0";

        // ── Version Check ────────────────────────────────────────────────────────

        public static async Task<UpdateInfo?> CheckForUpdateAsync(string apiBaseUrl)
        {
            try
            {
                string url = $"{apiBaseUrl.TrimEnd('/')}/api/version";
                string json = await _http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string latest   = root.GetProperty("version").GetString() ?? "";
                string download = root.GetProperty("downloadUrl").GetString() ?? "";
                string notes    = root.TryGetProperty("releaseNotes", out var rn) ? rn.GetString() ?? "" : "";

                bool newer = IsNewerVersion(latest, CurrentVersion);
                return new UpdateInfo(newer, latest, download, notes);
            }
            catch
            {
                return null;
            }
        }

        // ── Background Download ──────────────────────────────────────────────────

        /// <summary>
        /// Downloads the new exe to a temp staging path WITHOUT touching the running exe.
        /// App stays fully running. Reports 0.0–1.0 progress via callback.
        /// Returns the path of the staged file on success, or throws on failure.
        /// </summary>
        public static async Task<string> DownloadUpdateAsync(string downloadUrl, Action<double> onProgress)
        {
            string exePath  = Process.GetCurrentProcess().MainModule?.FileName
                              ?? Path.Combine(AppContext.BaseDirectory, "SystemCoreHost.exe");
            string dir      = Path.GetDirectoryName(exePath)!;
            string stagePath = Path.Combine(dir, "SystemCoreHost_pending.exe");

            // Clean up any previous failed download
            if (File.Exists(stagePath)) File.Delete(stagePath);

            using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long total = response.Content.Headers.ContentLength ?? -1;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var file   = File.Create(stagePath);

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

            // Validate — staged file must be a reasonable size (at least 100KB)
            var info = new FileInfo(stagePath);
            if (info.Length < 100_000)
            {
                File.Delete(stagePath);
                throw new InvalidDataException("Downloaded file is too small — may be corrupt.");
            }

            onProgress(1.0);
            return stagePath;
        }

        // ── Apply & Restart (user-triggered) ─────────────────────────────────────

        /// <summary>
        /// Writes a self-deleting batch script that swaps the staged exe for the live one
        /// after this process exits, then restarts the app.
        /// Only call this when the user explicitly clicks "Restart to Apply".
        /// The current exe is never touched until the process exits cleanly.
        /// </summary>
        public static void ApplyUpdateAndRestart(string stagedExePath)
        {
            string exePath  = Process.GetCurrentProcess().MainModule?.FileName
                              ?? Path.Combine(AppContext.BaseDirectory, "SystemCoreHost.exe");
            string dir      = Path.GetDirectoryName(exePath)!;
            string batchPath = Path.Combine(dir, "_shadow_apply_update.bat");

            // Escape paths for batch
            string batch = $@"@echo off
:waitloop
tasklist /FI ""IMAGENAME eq SystemCoreHost.exe"" 2>NUL | find /I ""SystemCoreHost.exe"" >NUL
if not errorlevel 1 (
    timeout /t 1 /nobreak >NUL
    goto waitloop
)
if not exist ""{stagedExePath}"" goto cleanup
move /Y ""{stagedExePath}"" ""{exePath}""
if errorlevel 1 goto cleanup
start """" ""{exePath}""
:cleanup
del ""%~f0""
";
            File.WriteAllText(batchPath, batch);

            Process.Start(new ProcessStartInfo
            {
                FileName        = batchPath,
                CreateNoWindow  = true,
                WindowStyle     = ProcessWindowStyle.Hidden,
                UseShellExecute = true
            });

            // Shut down cleanly — batch will restart once this process exits
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                System.Windows.Application.Current.Shutdown());
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static bool IsNewerVersion(string remote, string local)
        {
            if (Version.TryParse(remote, out var r) && Version.TryParse(local, out var l))
                return r > l;
            return string.Compare(remote, local, StringComparison.Ordinal) > 0;
        }
    }
}
