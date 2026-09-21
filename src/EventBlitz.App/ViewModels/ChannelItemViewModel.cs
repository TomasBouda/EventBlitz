using CommunityToolkit.Mvvm.ComponentModel;
using EventBlitz.Core.Models;

namespace EventBlitz.App.ViewModels;

/// <summary>A row of the channel sidebar: either a group header or a channel.</summary>
public abstract class SidebarRow : ObservableObject
{
}

/// <summary>Non-selectable section label ("WINDOWS LOGS · 5").</summary>
public sealed class ChannelGroupRow(string title, int count) : SidebarRow
{
    public string Title { get; } = title.ToUpperInvariant();
    public int Count { get; } = count;
}

/// <summary>One channel in the sidebar with its counters and favourite state.</summary>
public sealed partial class ChannelItemViewModel(ChannelInfo info) : SidebarRow
{
    public ChannelInfo Info { get; } = info;

    public string Name => Info.Name;
    public string DisplayName => Info.DisplayName;
    public string Folder => Info.Folder;
    public bool HasFolder => Info.Folder.Length > 0;
    public long? RecordCount => Info.RecordCount;
    public bool HasError => Info.AccessError is not null;
    public bool IsDisabled => !Info.IsEnabled;

    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>Second line: "12 345 events · 8.2 MB", or why the log cannot be read.</summary>
    public string Summary
    {
        get
        {
            var prefix = Info.Folder.Length > 0 ? Info.Folder + " · " : string.Empty;
            if (Info.AccessError is { } error) return prefix + error;
            if (!Info.IsEnabled) return prefix + "disabled";
            var count = Info.RecordCount ?? 0;
            var size = Converters.BytesConverter.Instance.Convert(Info.FileSize, typeof(string), null, System.Globalization.CultureInfo.InvariantCulture) as string;
            if (count == 0) return prefix + "empty";
            return prefix + $"{count:#,0} {(count == 1 ? "event" : "events")} · {size}".Replace(',', '\u2009');
        }
    }

    /// <summary>Text the sidebar search and the palette match against.</summary>
    public string SearchText => $"{Name} {DisplayName} {Folder}";
}

