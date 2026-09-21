using CommunityToolkit.Mvvm.ComponentModel;
using EventBlitz.Core.Models;

namespace EventBlitz.App.ViewModels;

/// <summary>One toggle of the level filter strip.</summary>
public sealed partial class LevelChip(LevelMask mask, string label, string shortLabel) : ObservableObject
{
    public LevelMask Mask { get; } = mask;
    public string Label { get; } = label;
    public string ShortLabel { get; } = shortLabel;

    [ObservableProperty]
    private bool _isSelected = true;

    public static LevelChip[] All() =>
    [
        new(LevelMask.Critical, "Critical", "CRIT"),
        new(LevelMask.Error, "Error", "ERR"),
        new(LevelMask.Warning, "Warning", "WARN"),
        new(LevelMask.Information, "Information", "INFO"),
        new(LevelMask.Verbose, "Verbose", "VERB"),
    ];
}

/// <summary>One entry of the time range picker.</summary>
public sealed record TimeRangeOption(string Key, string Label, TimeSpan? MaxAge)
{
    public static readonly TimeRangeOption[] All =
    [
        new("15m", "Last 15 minutes", TimeSpan.FromMinutes(15)),
        new("1h", "Last hour", TimeSpan.FromHours(1)),
        new("24h", "Last 24 hours", TimeSpan.FromHours(24)),
        new("7d", "Last 7 days", TimeSpan.FromDays(7)),
        new("30d", "Last 30 days", TimeSpan.FromDays(30)),
        new("all", "All time", null),
    ];

    public static TimeRangeOption ByKey(string? key) => Array.Find(All, o => o.Key == key) ?? All[2];

    public override string ToString() => Label;
}
