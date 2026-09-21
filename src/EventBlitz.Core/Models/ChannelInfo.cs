namespace EventBlitz.Core.Models;

/// <summary>An event log channel as listed in the sidebar.</summary>
public sealed class ChannelInfo
{
    public required string Name { get; init; }

    /// <summary>The last segment for compact display, e.g. "Kernel-Power" for "Microsoft-Windows-Kernel-Power/Thermal-Operational".</summary>
    public required string DisplayName { get; init; }

    /// <summary>"Windows Logs" for the five classic logs, otherwise "Applications and Services Logs".</summary>
    public required string Group { get; init; }

    /// <summary>Folder path the built-in viewer would show, e.g. "Microsoft / Windows / Kernel-Power".</summary>
    public required string Folder { get; init; }

    public bool IsEnabled { get; init; } = true;
    public long? RecordCount { get; init; }
    public long? FileSize { get; init; }
    public DateTime? LastWritten { get; init; }

    /// <summary>Set when the channel cannot be opened (typically the Security log without administrator rights).</summary>
    public string? AccessError { get; init; }

    public bool IsEmpty => RecordCount is null or 0;

    public const string WindowsLogsGroup = "Windows Logs";
    public const string ServicesLogsGroup = "Applications and Services Logs";

    private static readonly string[] WindowsLogs = ["Application", "Security", "Setup", "System", "ForwardedEvents"];

    public static bool IsWindowsLog(string name) => Array.Exists(WindowsLogs, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Derives group, folder and display name from a channel name the same way the built-in viewer nests it.</summary>
    public static (string Group, string Folder, string DisplayName) Describe(string name)
    {
        if (IsWindowsLog(name))
            return (WindowsLogsGroup, string.Empty, name);

        // "Microsoft-Windows-Kernel-Power/Thermal-Operational" -> folder Microsoft/Windows, display "Kernel-Power/Thermal-Operational"
        var slash = name.IndexOf('/');
        var provider = slash < 0 ? name : name[..slash];
        var stream = slash < 0 ? null : name[(slash + 1)..];

        var parts = provider.Split('-', 3, StringSplitOptions.RemoveEmptyEntries);
        string folder;
        string display;
        if (parts.Length == 3 && parts[0].Equals("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            folder = $"{parts[0]} / {parts[1]}";
            display = parts[2];
        }
        else if (parts.Length >= 2 && parts[0].Equals("Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            folder = parts[0];
            display = string.Join('-', parts.Skip(1));
        }
        else
        {
            folder = string.Empty;
            display = provider;
        }

        if (stream is not null)
            display = $"{display} / {stream}";

        return (ServicesLogsGroup, folder, display);
    }
}
