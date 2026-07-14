using System;
using System.IO;
using System.Text.Json;
using OverlayApp.Models;

namespace OverlayApp.Services
{
    /// <summary>
    /// Handles local persistence of user settings (API Keys, themes, opacity, notes) to a JSON file.
    /// Files are stored in the secure user-specific AppData folder to ensure write permissions.
    /// </summary>
    public class SettingsService
    {
        private readonly string _settingsFilePath;

        public SettingsService()
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OverlayApp");
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            _settingsFilePath = Path.Combine(folder, "settings.json");
        }

        /// <summary>
        /// Loads the settings from settings.json. Returns default settings if the file does not exist.
        /// </summary>
        public WidgetSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<WidgetSettings>(json, new JsonSerializerOptions { IncludeFields = true });
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception)
            {
                // Fallback to fresh default settings on load failure
            }
            return new WidgetSettings();
        }

        /// <summary>
        /// Serializes and writes settings to settings.json.
        /// </summary>
        public void SaveSettings(WidgetSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception)
            {
                // Mute errors during background auto-save
            }
        }
    }
}
