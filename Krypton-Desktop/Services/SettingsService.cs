using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace Krypton_Desktop.Services;

/// <summary>
/// Manages application settings persistence.
/// </summary>
public class SettingsService
{
    private static readonly JsonSerializerOptions CachedJsonOptions = new JsonSerializerOptions
    {
        WriteIndented = true
    };

    private readonly string _settingsPath;
    private AppSettings _settings;

    public AppSettings Settings => _settings;

    public SettingsService()
    {
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Krypton");

        Directory.CreateDirectory(appDataPath);
        _settingsPath = Path.Combine(appDataPath, "settings.json");

        _settings = Load();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, CachedJsonOptions);
            File.WriteAllText(_settingsPath, json);
            Log.Debug("Settings saved to {Path}", _settingsPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save settings");
        }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, CachedJsonOptions);
                if (settings != null)
                {
                    Log.Debug("Settings loaded from {Path}", _settingsPath);
                    return settings;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load settings, using defaults");
        }

        return new AppSettings();
    }
}

public class AppSettings
{
    // Hotkey settings
    public string Hotkey { get; set; } = "Ctrl+Shift+V";

    // History settings
    public int MaxHistoryItems { get; set; } = 100;

    // Server connection settings
    public string? ServerAddress { get; set; }
    public int ServerPort { get; set; } = 6789;
    public string? ApiKey { get; set; }
    public bool AutoConnect { get; set; } = true;

    // UI settings
    public bool StartMinimized { get; set; } = true;
    public bool StartWithWindows { get; set; } = false;

    // Search settings
    public bool AlwaysSearchServer { get; set; } = false;
}
