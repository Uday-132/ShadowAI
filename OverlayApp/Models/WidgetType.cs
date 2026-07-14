namespace OverlayApp.Models
{
    /// <summary>
    /// Represents the currently active widget type in the overlay container.
    /// </summary>
    public enum WidgetType
    {
        Notes,
        SystemMonitor,
        Timer,
        AiScan,      // kept for backward compatibility with saved settings (maps to TxtScan)
        TxtScan,
        VoiceScan
    }
}
