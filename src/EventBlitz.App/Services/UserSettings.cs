using System.Text.Json;
using System.Text.Json.Serialization;

namespace EventBlitz.App.Services;

/// <summary>Per-user preferences persisted as <c>settings.json</c> in the data folder.</summary>
public sealed class UserSettings
{
    private static readonly string SettingsPath = Path.Combine(AppInfo.DataDirectory, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>"Dark" or "Light"; null follows the operating system preference.</summary>
    public string? Theme { get; set; }

    /// <summary>"Stable" or "Nightly"; null follows the channel of the running build.</summary>
    public string? UpdateChannel { get; set; }

    /// <summary>Channels pinned to the top of the sidebar.</summary>
    public List<string> Favorites { get; set; } = new();

    /// <summary>Channels selected when the app was closed, restored on the next start.</summary>
    public List<string> SelectedChannels { get; set; } = new();

    /// <summary>Sidebar lists channels with no records only when this is on.</summary>
    public bool ShowEmptyChannels { get; set; }

    /// <summary>Time range key ("1h", "24h", "7d", "30d", "all") last used.</summary>
    public string TimeRange { get; set; } = "24h";

    /// <summary>Height of the detail pane in pixels.</summary>
    public double DetailHeight { get; set; } = 260;

    /// <summary>Log directory: absolute, or relative to the data folder; null = %LOCALAPPDATA%\EventBlitz\logs.</summary>
    public string? LogDirectory { get; set; }

    /// <summary>Minimum level written to the log files (Debug, Information, Warning, Error).</summary>
    public string LogLevel { get; set; } = "Information";

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath), JsonOptions) ?? new UserSettings();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read user settings: {ex.Message}");
        }

        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex)
        {
            AppLog.For("settings").Warning(ex, "Failed to write {Path}", SettingsPath);
        }
    }
}
