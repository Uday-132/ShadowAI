using System.Text.Json.Serialization;

namespace OverlayApp.Models
{
    /// <summary>
    /// Holds the user's overlay settings and persistent data.
    /// Fields use [JsonInclude] so System.Text.Json can serialize/deserialize them.
    /// </summary>
    public class WidgetSettings
    {
        [JsonInclude] public double Opacity = 0.9;
        [JsonInclude] public bool AlwaysOnTop = true;
        [JsonInclude] public bool IsClickThrough = false;
        [JsonInclude] public bool IsLocked = false;
        public WidgetType ActiveWidget = WidgetType.Notes; // Not persisted — always starts on Notes
        [JsonInclude] public string Theme = "Onyx";
        [JsonInclude] public string NotesText = "Welcome to Productivity Overlay!\n\nQuick Checklist:\n[ ] Research task\n[ ] Design interface\n[x] Initialize repo\n\nTips:\n- Adjust opacity with the slider in settings.\n- Click the Pin icon to make the overlay Click-Through.\n- Press global hotkey Ctrl+Shift+C to exit Click-Through mode!";
        [JsonInclude] public bool IsFirstRun = true;
        [JsonInclude] public double FontSize = 12.0;
        [JsonInclude] public string GroqKey = "";
        [JsonInclude] public string ScanResponseText = "";
        [JsonInclude] public string VoiceScanResponseText = "";
        [JsonInclude] public bool IsSystemAudioSource = false;
        [JsonInclude] public bool IsLiveMode = false;
        [JsonInclude] public string TextScanType = "Normal";
        [JsonInclude] public string SessionToken = "";
        [JsonInclude] public string UserEmail = "";
        [JsonInclude] public string ApiBaseUrl = "https://shadow-ai-seven.vercel.app";
    }
}
