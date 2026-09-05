using System;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OverlayApp.Helpers
{
    public class LlmErrorInfo
    {
        public bool IsError { get; set; }
        public string FriendlyMessage { get; set; } = "";
        public bool RequiresKeyCheck { get; set; }
        public bool IsRateLimit { get; set; }
        public bool IsEnvironmentInterrupted { get; set; }
        public string RawError { get; set; } = "";
    }

    public static class LlmErrorHelper
    {
        /// <summary>
        /// Translates raw API error responses, HTTP status codes, and exceptions into clear, actionable, user-friendly messages.
        /// </summary>
        public static LlmErrorInfo FormatError(string provider, string modelName, int statusCode, string rawResponse, Exception? ex = null)
        {
            var info = new LlmErrorInfo { IsError = true, RawError = rawResponse ?? "" };

            // 1. Exception Handling (Network drop, Anti-detect / Exam lockdown interruption, SSL, Timeout)
            if (ex != null)
            {
                string msg = ex.Message.ToLowerInvariant();
                if (ex is System.Net.Http.HttpRequestException ||
                    ex is System.Threading.Tasks.TaskCanceledException ||
                    ex is TimeoutException ||
                    msg.Contains("ssl") || msg.Contains("connection") || msg.Contains("host") || 
                    msg.Contains("reset") || msg.Contains("aborted") || msg.Contains("proxy") ||
                    msg.Contains("interrupted") || msg.Contains("unreachable"))
                {
                    info.IsEnvironmentInterrupted = true;
                    info.FriendlyMessage = $"⚠️ Connection interrupted by browser or network environment ({provider}). Please check your extension settings, proxy, or firewall configuration.";
                    System.Diagnostics.Debug.WriteLine($"[Exam/Environment Interruption] {provider} ({modelName}): {ex.Message}");
                    return info;
                }

                info.FriendlyMessage = $"⚠️ {provider} connection error: {ex.Message}";
                return info;
            }

            rawResponse ??= "";

            // Extract inner human-readable message from JSON if present
            string extractedMsg = ExtractJsonErrorMessage(rawResponse);

            // 2. Rate Limit (HTTP 429, rate_limit_exceeded, RESOURCE_EXHAUSTED)
            if (statusCode == 429 || 
                rawResponse.Contains("rate_limit_exceeded", StringComparison.OrdinalIgnoreCase) ||
                rawResponse.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) ||
                rawResponse.Contains("tokens per day", StringComparison.OrdinalIgnoreCase) ||
                rawResponse.Contains("tokens per minute", StringComparison.OrdinalIgnoreCase))
            {
                info.IsRateLimit = true;
                info.RequiresKeyCheck = true;

                string waitText = "";
                var waitMatch = Regex.Match(extractedMsg, @"try again in ([0-9\.]+m?s?)", RegexOptions.IgnoreCase);
                if (waitMatch.Success)
                {
                    string secRaw = waitMatch.Groups[1].Value.Replace("s", "");
                    if (double.TryParse(secRaw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double sec))
                    {
                        waitText = $" Please wait ~{(int)Math.Ceiling(sec)}s.";
                    }
                    else
                    {
                        waitText = $" Please wait ~{waitMatch.Groups[1].Value}.";
                    }
                }

                string quotaDetail = "";
                if (extractedMsg.Contains("tokens per day (TPD)", StringComparison.OrdinalIgnoreCase))
                {
                    quotaDetail = " Daily token quota (TPD) reached.";
                }
                else if (extractedMsg.Contains("tokens per minute (TPM)", StringComparison.OrdinalIgnoreCase))
                {
                    quotaDetail = " Per-minute token limit (TPM) reached.";
                }

                info.FriendlyMessage = $"⏳ Rate limit reached ({modelName}).{quotaDetail}{waitText} Upgrade to Dev Tier or switch API key in Settings.";
                return info;
            }

            // 3. Authentication & Key Errors (HTTP 401, 403, invalid_api_key, API_KEY_INVALID)
            if (statusCode == 401 || statusCode == 403 ||
                rawResponse.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase) ||
                rawResponse.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) ||
                rawResponse.Contains("API key not valid", StringComparison.OrdinalIgnoreCase) ||
                rawResponse.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase) ||
                rawResponse.Contains("PERMISSION_DENIED", StringComparison.OrdinalIgnoreCase))
            {
                info.RequiresKeyCheck = true;
                info.FriendlyMessage = $"🔑 Invalid or expired {provider} API key (HTTP {statusCode}). Please check or update your key in Settings.";
                return info;
            }

            // 4. Server Overload / Downtime (HTTP 500, 502, 503, 504)
            if (statusCode >= 500 && statusCode < 600)
            {
                info.FriendlyMessage = $"☁️ {provider} server temporarily overloaded (HTTP {statusCode}). Please retry in a few moments.";
                return info;
            }

            // 5. Context size / Request entity too large (HTTP 413)
            if (statusCode == 413 || 
                rawResponse.Contains("RequestEntityTooLarge", StringComparison.OrdinalIgnoreCase) || 
                rawResponse.Contains("context length", StringComparison.OrdinalIgnoreCase) ||
                rawResponse.Contains("maximum context length", StringComparison.OrdinalIgnoreCase))
            {
                info.FriendlyMessage = $"📄 Input too large for {modelName}. Try capturing fewer screenshots or clearing previous conversation turns.";
                return info;
            }

            // 6. Generic Sanitized Message
            if (!string.IsNullOrWhiteSpace(extractedMsg))
            {
                // Remove billing / promotional links and strip long technical IDs
                string cleanMsg = Regex.Replace(extractedMsg, @"https?://\S+", "").Trim();
                cleanMsg = Regex.Replace(cleanMsg, @"in organization `[^`]+`", "").Trim();
                cleanMsg = Regex.Replace(cleanMsg, @"service tier `[^`]+`", "").Trim();
                cleanMsg = cleanMsg.Replace("  ", " ");

                if (cleanMsg.Length > 140) cleanMsg = cleanMsg.Substring(0, 137) + "...";
                info.FriendlyMessage = $"⚠️ {provider} Error: {cleanMsg}";
                return info;
            }

            info.FriendlyMessage = $"⚠️ {provider} API returned HTTP {statusCode}. Please verify your network and API keys.";
            return info;
        }

        private static string ExtractJsonErrorMessage(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var errProp))
                {
                    if (errProp.ValueKind == JsonValueKind.Object && errProp.TryGetProperty("message", out var msgProp))
                    {
                        return msgProp.GetString() ?? "";
                    }
                    if (errProp.ValueKind == JsonValueKind.String)
                    {
                        return errProp.GetString() ?? "";
                    }
                }
                if (root.TryGetProperty("message", out var directMsg))
                {
                    return directMsg.GetString() ?? "";
                }
            }
            catch {}
            return raw.Length > 200 ? raw.Substring(0, 197) + "..." : raw;
        }

        /// <summary>
        /// Checks if a returned response string represents an error condition rather than a successful AI answer.
        /// </summary>
        public static bool IsErrorResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            string t = text.Trim();
            return t.StartsWith("⚠️") || 
                   t.StartsWith("❌") || 
                   t.StartsWith("⏳ Rate limit") || 
                   t.StartsWith("🔑 Invalid") ||
                   t.StartsWith("☁️") ||
                   t.StartsWith("Groq API Error", StringComparison.OrdinalIgnoreCase) ||
                   t.StartsWith("Gemini API Error", StringComparison.OrdinalIgnoreCase) ||
                   t.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
                   t.StartsWith("Error contacting", StringComparison.OrdinalIgnoreCase);
        }
    }
}
