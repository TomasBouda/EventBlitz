using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EventBlitz.App.Services;
using EventBlitz.Core.Models;
using TomLabs.AutoUpdate;

namespace EventBlitz.App.ViewModels;

/// <summary>One entry of the command palette: a channel to open, a filter preset, or an action.</summary>
public sealed record PaletteItem(string Group, string Icon, string Title, string Hint, string Shortcut, Action Run);

/// <summary>
/// Ctrl+K palette: fuzzy search across channels, filters and actions.
/// Items are rebuilt every time the palette opens so they reflect the current state.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly MainWindowViewModel _main;
    private readonly List<PaletteItem> _all = new();

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private ObservableCollection<PaletteItem> _items = new();

    [ObservableProperty]
    private PaletteItem? _selectedItem;

    private string _query = string.Empty;
    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value))
                Filter();
        }
    }

    /// <summary>Channels the palette can jump to; filled by the main view model once they are listed.</summary>
    public Func<IEnumerable<ChannelItemViewModel>>? ChannelsProvider { get; set; }

    public CommandPaletteViewModel(MainWindowViewModel main)
    {
        _main = main;
    }

    public void Open()
    {
        BuildItems();
        Query = string.Empty;
        Filter();
        IsOpen = true;
    }

    public void Close() => IsOpen = false;

    public void MoveSelection(int delta)
    {
        if (Items.Count == 0) return;
        var index = SelectedItem is null ? -1 : Items.IndexOf(SelectedItem);
        index = Math.Clamp(index + delta, 0, Items.Count - 1);
        SelectedItem = Items[index];
    }

    public void RunSelected()
    {
        var item = SelectedItem ?? Items.FirstOrDefault();
        if (item is null) return;
        Close();
        item.Run();
    }

    private void BuildItems()
    {
        _all.Clear();
        var vm = _main;

        if (vm.SelectedEvent is { } ev)
        {
            _all.Add(new PaletteItem("Event", "fa-solid fa-copy", "Copy event", $"{ev.Provider} · {ev.EventId}", "Ctrl+C", () => vm.CopyEventCommand.Execute(ev)));
            _all.Add(new PaletteItem("Event", "fa-solid fa-hashtag", $"Only event ID {ev.EventId}", ev.Provider, "", () => vm.FilterByEventIdCommand.Execute(ev)));
            _all.Add(new PaletteItem("Event", "fa-solid fa-microchip", $"Only provider {ev.Provider}", "filter by provider", "", () => vm.FilterByProviderCommand.Execute(ev)));
        }

        _all.Add(new PaletteItem("View", vm.IsLive ? "fa-solid fa-pause" : "fa-solid fa-play", vm.IsLive ? "Stop live tail" : "Start live tail",
            "new events appear at the top", "Ctrl+L", () => vm.ToggleLiveCommand.Execute(null)));
        _all.Add(new PaletteItem("View", "fa-solid fa-arrows-rotate", "Reload events", "run the query again", "F5", () => vm.RunQueryCommand.Execute(null)));
        _all.Add(new PaletteItem("View", "fa-solid fa-filter-circle-xmark", "Clear filters", "all levels, no text", "Ctrl+Shift+X", () => vm.ClearFiltersCommand.Execute(null)));
        _all.Add(new PaletteItem("View", "fa-solid fa-magnifying-glass", "Search events…", "focus the search box", "Ctrl+F", () => vm.FocusSearch?.Invoke()));
        _all.Add(new PaletteItem("View", "fa-solid fa-angles-down", "Load more", "next page of the query", "", () => vm.LoadMoreCommand.Execute(null)));

        foreach (var chip in vm.Levels)
            _all.Add(new PaletteItem("Level", "fa-solid fa-layer-group", $"Only {chip.Label.ToLowerInvariant()}", "level filter", "", () => vm.SelectOnlyLevel(chip.Mask)));
        _all.Add(new PaletteItem("Level", "fa-solid fa-layer-group", "Errors and warnings", "critical + error + warning", "", () =>
        {
            foreach (var chip in vm.Levels)
                chip.IsSelected = chip.Mask is LevelMask.Critical or LevelMask.Error or LevelMask.Warning;
        }));
        _all.Add(new PaletteItem("Level", "fa-solid fa-layer-group", "All levels", "level filter", "", () =>
        {
            foreach (var chip in vm.Levels) chip.IsSelected = true;
        }));

        foreach (var range in vm.TimeRanges)
            _all.Add(new PaletteItem("Time", "fa-solid fa-clock", range.Label, "time range", "", () => vm.SelectedTimeRange = range));

        foreach (var channel in ChannelsProvider?.Invoke() ?? Enumerable.Empty<ChannelItemViewModel>())
        {
            var captured = channel;
            _all.Add(new PaletteItem("Channel", "fa-solid fa-list", captured.DisplayName, captured.Name, "", () => vm.SelectChannels([captured.Name])));
            if (!vm.SelectedChannelNames.Contains(captured.Name))
                _all.Add(new PaletteItem("Channel", "fa-solid fa-plus", $"Add {captured.DisplayName}", "to the merged view", "", () => vm.SelectChannels(vm.SelectedChannelNames.Append(captured.Name))));
        }
        _all.Add(new PaletteItem("Channel", "fa-solid fa-layer-group", "Application + System", "the usual pair", "", () => vm.SelectChannels(["Application", "System"])));

        _all.Add(new PaletteItem("Alerts", "fa-regular fa-bell", vm.ShowAlerts ? "Back to events" : "Alerts…", $"{vm.Alerts.Rules.Count} rules · {vm.Alerts.ActiveCount} active", "Ctrl+Shift+A", () => vm.ToggleAlertsCommand.Execute(null)));
        _all.Add(new PaletteItem("Alerts", "fa-solid fa-plus", "New alert from current view", "sound + toast for what you are looking at", "", () => { vm.Alerts.AddFromViewCommand.Execute(null); vm.ShowAlerts = true; }));
        _all.Add(new PaletteItem("Alerts", vm.Alerts.IsMuted ? "fa-regular fa-bell" : "fa-regular fa-bell-slash", vm.Alerts.IsMuted ? "Unmute alerts" : "Mute alerts", "sound only; toasts stay", "", () => vm.Alerts.ToggleMuteCommand.Execute(null)));

        _all.Add(new PaletteItem("App", "fa-solid fa-arrows-rotate", "Refresh channels", "re-read sizes and counts", "", () => vm.RefreshChannelsCommand.Execute(null)));
        _all.Add(new PaletteItem("App", "fa-solid fa-eye", vm.ShowEmptyChannels ? "Hide empty channels" : "Show empty channels", "sidebar", "", () => vm.ShowEmptyChannels = !vm.ShowEmptyChannels));
        _all.Add(new PaletteItem("App", "fa-solid fa-circle-half-stroke", "Toggle light / dark", "remembered for next start", "Ctrl+Shift+L", () => vm.ToggleThemeCommand.Execute(null)));
        _all.Add(new PaletteItem("App", "fa-solid fa-file-lines", vm.ShowAppLog ? "Back to events" : "Open app log", AppLog.Directory, "", () => vm.ToggleAppLogCommand.Execute(null)));
        if (!vm.IsAdministrator)
            _all.Add(new PaletteItem("App", "fa-solid fa-shield-halved", "Restart as administrator", "needed for the Security log", "", () => vm.RestartAsAdministratorCommand.Execute(null)));

        if (Updater.Current is { } updater)
        {
            var build = $"{updater.Build.Version} · {updater.Channel.ToString().ToLowerInvariant()} channel";
            _all.Add(new PaletteItem("App", "fa-solid fa-cloud-arrow-down", "Check for updates", build, "", () => _ = updater.CheckAsync()));
            var other = updater.Channel == UpdateChannel.Stable ? UpdateChannel.Nightly : UpdateChannel.Stable;
            _all.Add(new PaletteItem("App", "fa-solid fa-code-branch", $"Switch to {other.ToString().ToLowerInvariant()} updates",
                other == UpdateChannel.Nightly ? "latest commit on master" : "tagged releases only", "", () => updater.Channel = other));
        }
    }

    private void Filter()
    {
        var query = Query.Trim();
        IEnumerable<PaletteItem> result = string.IsNullOrEmpty(query)
            ? _all
            : _all.Select(i => (item: i, score: Score(i, query)))
                  .Where(t => t.score > 0)
                  .OrderByDescending(t => t.score)
                  .Select(t => t.item);

        Items = new ObservableCollection<PaletteItem>(result.Take(14));
        SelectedItem = Items.FirstOrDefault();
    }

    /// <summary>Simple fuzzy score: prefix beats substring beats subsequence; the hint counts half.</summary>
    private static int Score(PaletteItem item, string query)
    {
        var title = Score(item.Title, query);
        var hint = Score(item.Hint, query) / 2;
        return Math.Max(title, hint);
    }

    private static int Score(string text, string query)
    {
        if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 300;
        if (text.Contains(query, StringComparison.OrdinalIgnoreCase)) return 200;

        var qi = 0;
        foreach (var ch in text)
        {
            if (qi < query.Length && char.ToLowerInvariant(ch) == char.ToLowerInvariant(query[qi]))
                qi++;
        }
        return qi == query.Length ? 100 : 0;
    }
}
