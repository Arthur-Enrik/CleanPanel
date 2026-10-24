using System.IO;
using System.Text.Json;
using HardwareOverlayApp.Models;

namespace HardwareOverlayApp.Services;

public class SettingsService
{
    private readonly string _settingsFilePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public SettingsService()
    {
        _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                var defaultSettings = new AppSettings();
                Save(defaultSettings);
                return defaultSettings;
            }

            string json = File.ReadAllText(_settingsFilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();

            // Normaliza caso um arquivo antigo tenha salvo em escala 10..100
            if (settings.OverlayOpacity > 1.0)
            {
                settings.OverlayOpacity /= 100.0;
            }

            settings.OverlayOpacity = Math.Clamp(settings.OverlayOpacity, 0.1, 1.0);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            settings.OverlayOpacity = Math.Clamp(Math.Round(settings.OverlayOpacity, 2), 0.1, 1.0);
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_settingsFilePath, json);
        }
        catch
        {
            // Ignora falhas transitórias de escrita em disco
        }
    }
}
