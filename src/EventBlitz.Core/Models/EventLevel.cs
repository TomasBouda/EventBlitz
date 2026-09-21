namespace EventBlitz.Core.Models;

/// <summary>Severity of an event as the Windows Event Log stores it in <c>System/Level</c>.</summary>
public enum EventLevel
{
    /// <summary>Level 0: written regardless of the provider's level setting; shown as Information.</summary>
    LogAlways = 0,
    Critical = 1,
    Error = 2,
    Warning = 3,
    Information = 4,
    Verbose = 5,
}

/// <summary>Set of levels a filter lets through.</summary>
[Flags]
public enum LevelMask
{
    None = 0,
    Critical = 1 << 1,
    Error = 1 << 2,
    Warning = 1 << 3,
    Information = 1 << 4,
    Verbose = 1 << 5,
    All = Critical | Error | Warning | Information | Verbose,
}

public static class EventLevelExtensions
{
    /// <summary>Display name; LogAlways reads as Information like the built-in viewer shows it.</summary>
    public static string DisplayName(this EventLevel level) => level switch
    {
        EventLevel.Critical => "Critical",
        EventLevel.Error => "Error",
        EventLevel.Warning => "Warning",
        EventLevel.Verbose => "Verbose",
        _ => "Information",
    };

    public static LevelMask ToMask(this EventLevel level) => level switch
    {
        EventLevel.Critical => LevelMask.Critical,
        EventLevel.Error => LevelMask.Error,
        EventLevel.Warning => LevelMask.Warning,
        EventLevel.Verbose => LevelMask.Verbose,
        _ => LevelMask.Information,
    };

    public static bool Contains(this LevelMask mask, EventLevel level) => (mask & level.ToMask()) != 0;
}
