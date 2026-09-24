using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.Selection;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EventBlitz.App.Services;
using EventBlitz.Core.Models;
using EventBlitz.Core.Services;
using Serilog;
using TomLabs.AutoUpdate;

namespace EventBlitz.App.ViewModels;

/// <summary>
/// The whole window: channel sidebar, filter strip, event list, detail pane, live tail, status bar and the palette.
/// Reading the log runs on the thread pool; everything observable is touched on the UI thread only.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    /// <summary>Events read per "page"; the list grows by this much on Load more.</summary>
    private const int PageSize = 1000;

    /// <summary>Matches handed to the UI at a time while a page loads, so a long search paints progressively.</summary>
    private const int ChunkSize = 100;

    /// <summary>Live tail keeps at most this many rows so an event storm cannot eat the memory.</summary>
    private const int MaxRows = 50_000;

    private static readonly ILogger Log = AppLog.For("main");

    private readonly IEventLogSource _source;
    private readonly AlertService _alertService;
    private readonly UserSettings _settings;
    private readonly List<ChannelItemViewModel> _allChannels = new();
    private readonly DispatcherTimer _queryDebounce;
    private readonly DispatcherTimer _progressTimer;

    private IEventQuery? _query;
    private CancellationTokenSource? _queryCts;
    private IDisposable? _watcher;
    private int _queryGeneration;
    private bool _suppressSelectionEvents;

    public MainWindowViewModel(IEventLogSource source, UserSettings settings)
    {
        _source = source;
        _settings = settings;
        _alertService = new AlertService(source);
        _alertService.Triggered += OnAlertTriggered;
        Alerts = new AlertsViewModel(_alertService, settings) { CurrentView = () => (SelectedChannelNames, BuildFilter()) };

        _queryDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _queryDebounce.Tick += (_, _) =>
        {
            _queryDebounce.Stop();
            RunQuery();
        };
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _progressTimer.Tick += (_, _) => UpdateLoadingStatus();

        Palette = new CommandPaletteViewModel(this) { ChannelsProvider = () => _allChannels };
        AppLogPage = new LogsViewModel();
        ShowEmptyChannels = settings.ShowEmptyChannels;
        DetailHeight = settings.DetailHeight;
        SelectedTimeRange = TimeRangeOption.ByKey(settings.TimeRange);

        foreach (var chip in Levels)
            chip.PropertyChanged += (_, _) => ScheduleQuery();

        Events.CollectionChanged += (_, _) => OnPropertyChanged(nameof(EventCountText));
        ChannelSelection.SingleSelect = false;
        ChannelSelection.SelectionChanged += OnChannelSelectionChanged;

        if (Updater.Current is { } updater)
        {
            updater.StateChanged += (_, _) => UpdateStatus = DescribeUpdater(updater);
            UpdateStatus = DescribeUpdater(updater);
        }

        _ = LoadChannelsAsync();
    }

    // ----- Shell -----

    public CommandPaletteViewModel Palette { get; }

    public LogsViewModel AppLogPage { get; }

    public string AppVersion => AppInfo.VersionLabel;

    public string MachineName { get; } = Environment.MachineName;

    public bool IsAdministrator => AppInfo.IsAdministrator;

    /// <summary>The app's own log page replaces the event view while on.</summary>
    [ObservableProperty]
    private bool _showAppLog;

    /// <summary>Last outcome of the updater for the status bar.</summary>
    [ObservableProperty]
    private string _updateStatus = string.Empty;

    /// <summary>Right side of the status bar: what the app is doing or did last.</summary>
    [ObservableProperty]
    private string _statusText = "Reading channels…";

    /// <summary>Set by the window: puts text on the clipboard.</summary>
    public Func<string, Task>? CopyToClipboard { get; set; }

    /// <summary>Set by the window: focuses the search box.</summary>
    public Action? FocusSearch { get; set; }

    // ----- Channels -----

    public ObservableCollection<SidebarRow> ChannelRows { get; } = new();

    public SelectionModel<SidebarRow> ChannelSelection { get; } = new();

    [ObservableProperty]
    private string _channelSearch = string.Empty;

    [ObservableProperty]
    private bool _showEmptyChannels;

    [ObservableProperty]
    private bool _isLoadingChannels = true;

    [ObservableProperty]
    private int _channelCount;

    /// <summary>Names of the selected channels, in sidebar order.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _selectedChannelNames = Array.Empty<string>();

    public bool HasSelection => SelectedChannelNames.Count > 0;

    public bool IsMergedView => SelectedChannelNames.Count > 1;

    /// <summary>"Application" or "Application + System + 2 more" for the title row.</summary>
    public string SelectionTitle => SelectedChannelNames.Count switch
    {
        0 => string.Empty,
        1 => _allChannels.Find(c => c.Name == SelectedChannelNames[0])?.DisplayName ?? SelectedChannelNames[0],
        <= 3 => string.Join(" + ", SelectedChannelNames.Select(n => _allChannels.Find(c => c.Name == n)?.DisplayName ?? n)),
        _ => $"{string.Join(" + ", SelectedChannelNames.Take(2).Select(n => _allChannels.Find(c => c.Name == n)?.DisplayName ?? n))} + {SelectedChannelNames.Count - 2} more",
    };

    /// <summary>Full channel names under the title, e.g. "Microsoft-Windows-Kernel-Power/Thermal-Operational".</summary>
    public string SelectionSubtitle => string.Join(" · ", SelectedChannelNames);

    public ChannelItemViewModel? SingleSelectedChannel =>
        SelectedChannelNames.Count == 1 ? _allChannels.Find(c => c.Name == SelectedChannelNames[0]) : null;

    partial void OnChannelSearchChanged(string value) => RebuildChannelRows();

    partial void OnShowEmptyChannelsChanged(bool value)
    {
        _settings.ShowEmptyChannels = value;
        _settings.Save();
        RebuildChannelRows();
    }

    partial void OnSelectedChannelNamesChanged(IReadOnlyList<string> value)
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsMergedView));
        OnPropertyChanged(nameof(SelectionTitle));
        OnPropertyChanged(nameof(SelectionSubtitle));
        OnPropertyChanged(nameof(SingleSelectedChannel));
    }

    private async Task LoadChannelsAsync()
    {
        IsLoadingChannels = true;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var channels = await Task.Run(() => _source.ListChannels((done, total) =>
            {
                if (done % 25 == 0 || done == total)
                    Dispatcher.UIThread.Post(() => StatusText = $"Reading channels {done}/{total}…");
            }));

            _allChannels.Clear();
            foreach (var info in channels)
                _allChannels.Add(new ChannelItemViewModel(info) { IsFavorite = _settings.Favorites.Contains(info.Name) });
            ChannelCount = _allChannels.Count;
            Log.Information("{Count} channels listed in {Elapsed} ms", channels.Count, stopwatch.ElapsedMilliseconds);
            StatusText = $"{channels.Count} channels";

            RebuildChannelRows();

            // Restore the last selection, or start on the Application log like everyone does.
            var wanted = _settings.SelectedChannels.Where(n => _allChannels.Any(c => c.Name == n)).ToList();
            if (wanted.Count == 0 && _allChannels.Any(c => c.Name == "Application"))
                wanted.Add("Application");
            SelectChannels(wanted);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Listing channels failed");
            StatusText = "Cannot list channels: " + ex.Message;
        }
        finally
        {
            IsLoadingChannels = false;
        }
    }

    /// <summary>Rebuilds the sidebar rows from the search text, favourites and the empty-channel switch, keeping the selection.</summary>
    private void RebuildChannelRows()
    {
        var selected = SelectedChannelNames;
        var search = ChannelSearch.Trim();
        var visible = _allChannels
            .Where(c => ShowEmptyChannels || !c.Info.IsEmpty || c.HasError || c.IsFavorite || selected.Contains(c.Name))
            .Where(c => search.Length == 0 || c.SearchText.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var rows = new List<SidebarRow>();
        void AddGroup(string title, IEnumerable<ChannelItemViewModel> items)
        {
            var list = items.ToList();
            if (list.Count == 0) return;
            rows.Add(new ChannelGroupRow(title, list.Count));
            rows.AddRange(list);
        }

        AddGroup("Favorites", visible.Where(c => c.IsFavorite));
        AddGroup(ChannelInfo.WindowsLogsGroup, visible.Where(c => !c.IsFavorite && c.Info.Group == ChannelInfo.WindowsLogsGroup));
        AddGroup(ChannelInfo.ServicesLogsGroup, visible.Where(c => !c.IsFavorite && c.Info.Group == ChannelInfo.ServicesLogsGroup));

        _suppressSelectionEvents = true;
        try
        {
            ChannelRows.Clear();
            foreach (var row in rows)
                ChannelRows.Add(row);

            ChannelSelection.Clear();
            for (var i = 0; i < ChannelRows.Count; i++)
            {
                if (ChannelRows[i] is ChannelItemViewModel c && selected.Contains(c.Name))
                    ChannelSelection.Select(i);
            }
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
    }

    private void OnChannelSelectionChanged(object? sender, SelectionModelSelectionChangedEventArgs<SidebarRow> e)
    {
        if (_suppressSelectionEvents) return;

        // Group headers are not channels: undo a click on one right away.
        var headers = ChannelSelection.SelectedIndexes.Where(i => ChannelRows[i] is ChannelGroupRow).ToList();
        if (headers.Count > 0)
        {
            _suppressSelectionEvents = true;
            foreach (var index in headers)
                ChannelSelection.Deselect(index);
            _suppressSelectionEvents = false;
        }

        var names = ChannelSelection.SelectedItems.OfType<ChannelItemViewModel>().Select(c => c.Name).ToList();
        if (names.SequenceEqual(SelectedChannelNames)) return;

        SelectedChannelNames = names;
        _settings.SelectedChannels = names;
        _settings.Save();
        ScheduleQuery();
    }

    /// <summary>Selects exactly these channels (palette, automation, restored session).</summary>
    public void SelectChannels(IEnumerable<string> names)
    {
        var wanted = names.Where(n => _allChannels.Any(c => c.Name == n)).Distinct().ToList();
        _suppressSelectionEvents = true;
        try
        {
            // A channel hidden by the search or the empty filter must show up to be selectable.
            if (wanted.Any(n => !ChannelRows.OfType<ChannelItemViewModel>().Any(c => c.Name == n)))
            {
                ChannelSearch = string.Empty;
                SelectedChannelNames = wanted;
                RebuildChannelRows();
            }

            ChannelSelection.Clear();
            for (var i = 0; i < ChannelRows.Count; i++)
            {
                if (ChannelRows[i] is ChannelItemViewModel c && wanted.Contains(c.Name))
                    ChannelSelection.Select(i);
            }
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        SelectedChannelNames = wanted;
        _settings.SelectedChannels = wanted;
        _settings.Save();
        ScheduleQuery();
    }

    [RelayCommand]
    private void ToggleFavorite(ChannelItemViewModel? channel)
    {
        if (channel is null) return;
        channel.IsFavorite = !channel.IsFavorite;
        if (channel.IsFavorite) _settings.Favorites.Add(channel.Name);
        else _settings.Favorites.Remove(channel.Name);
        _settings.Save();
        RebuildChannelRows();
    }

    [RelayCommand]
    private async Task RefreshChannels()
    {
        await LoadChannelsAsync();
    }

    // ----- Filter -----

    public LevelChip[] Levels { get; } = LevelChip.All();

    public TimeRangeOption[] TimeRanges => TimeRangeOption.All;

    [ObservableProperty]
    private TimeRangeOption _selectedTimeRange;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _eventIdText = string.Empty;

    [ObservableProperty]
    private string _providerText = string.Empty;

    /// <summary>Why the event ID text cannot be used, or null.</summary>
    [ObservableProperty]
    private string? _eventIdError;

    public bool HasActiveFilter =>
        !string.IsNullOrWhiteSpace(SearchText) || !string.IsNullOrWhiteSpace(EventIdText) || !string.IsNullOrWhiteSpace(ProviderText)
        || Levels.Any(l => !l.IsSelected);

    partial void OnSelectedTimeRangeChanged(TimeRangeOption value)
    {
        _settings.TimeRange = value.Key;
        _settings.Save();
        ScheduleQuery();
    }

    partial void OnSearchTextChanged(string value) => ScheduleQuery();
    partial void OnProviderTextChanged(string value) => ScheduleQuery();

    partial void OnEventIdTextChanged(string value)
    {
        EventIdError = EventIdSpec.TryParse(value, out _, out var bad) ? null : $"Not an event ID: {bad}";
        ScheduleQuery();
    }

    private EventFilter BuildFilter()
    {
        var mask = Levels.Where(l => l.IsSelected).Aggregate(LevelMask.None, (m, l) => m | l.Mask);
        if (mask == LevelMask.None) mask = LevelMask.All;
        EventIdSpec.TryParse(EventIdText, out var ids, out _);
        return new EventFilter
        {
            Levels = mask,
            MaxAge = SelectedTimeRange.MaxAge,
            EventIds = ids ?? EventIdSpec.Empty,
            Provider = ProviderText,
            Text = SearchText,
        };
    }

    [RelayCommand]
    private void ClearFilters()
    {
        foreach (var chip in Levels) chip.IsSelected = true;
        SearchText = string.Empty;
        EventIdText = string.Empty;
        ProviderText = string.Empty;
    }

    /// <summary>Narrows the list to one event ID (detail pane action).</summary>
    [RelayCommand]
    private void FilterByEventId(EventItem? item)
    {
        if (item is not null) EventIdText = item.EventId.ToString();
    }

    [RelayCommand]
    private void FilterByProvider(EventItem? item)
    {
        if (item is not null) ProviderText = item.Provider;
    }

    /// <summary>Sets the level strip to exactly one level; the palette offers this per level.</summary>
    public void SelectOnlyLevel(LevelMask mask)
    {
        foreach (var chip in Levels)
            chip.IsSelected = chip.Mask == mask;
    }

    // ----- Events -----

    public ObservableCollection<EventItem> Events { get; } = new();

    [ObservableProperty]
    private EventItem? _selectedEvent;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>True while the current query still has events to read.</summary>
    [ObservableProperty]
    private bool _canLoadMore;

    [ObservableProperty]
    private long _scannedCount;

    /// <summary>Channels the query could not open, shown as a hint above the list.</summary>
    [ObservableProperty]
    private string? _queryError;

    /// <summary>Left side of the status bar.</summary>
    [ObservableProperty]
    private string _countText = string.Empty;

    public bool IsEmpty => Events.Count == 0 && !IsLoading && HasSelection;

    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(IsEmpty));

    private void ScheduleQuery()
    {
        OnPropertyChanged(nameof(HasActiveFilter));
        _queryDebounce.Stop();
        _queryDebounce.Start();
    }

    /// <summary>Discards the current list and reads the first page for the selected channels and filter.</summary>
    [RelayCommand]
    private void RunQuery()
    {
        _queryDebounce.Stop();
        var generation = ++_queryGeneration;
        _queryCts?.Cancel();
        _queryCts = new CancellationTokenSource();
        var token = _queryCts.Token;
        var previous = _query;
        _query = null;
        StopWatcher();

        Events.Clear();
        SelectedEvent = null;
        QueryError = null;
        ScannedCount = 0;
        CanLoadMore = false;
        LiveCount = 0;
        OnPropertyChanged(nameof(IsEmpty));

        var channels = SelectedChannelNames;
        if (channels.Count == 0)
        {
            CountText = string.Empty;
            StatusText = "Select a channel";
            IsLoading = false;
            _ = Task.Run(() => previous?.Dispose());
            return;
        }

        var filter = BuildFilter();
        Log.Debug("Query {Channels} with {XPath} text={Text} provider={Provider}", string.Join(",", channels), filter.ToXPath(), filter.Text, filter.Provider);
        IsLoading = true;
        _progressTimer.Start();
        StatusText = "Reading…";

        _ = Task.Run(async () =>
        {
            previous?.Dispose();
            IEventQuery query;
            try
            {
                query = _source.Query(channels, filter);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Opening the query failed");
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (generation != _queryGeneration) return;
                    IsLoading = false;
                    _progressTimer.Stop();
                    StatusText = ex.Message;
                });
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (generation == _queryGeneration) _query = query;
                else query.Dispose();
            });
            if (generation != _queryGeneration) return;

            await ReadPageAsync(query, generation, token);
            if (generation == _queryGeneration && IsLive)
                Dispatcher.UIThread.Post(StartWatcher);
        }, token);
    }

    /// <summary>Reads the next page of the current query and appends it.</summary>
    [RelayCommand]
    private void LoadMore()
    {
        if (_query is not { } query || IsLoading || query.IsExhausted) return;
        var generation = _queryGeneration;
        var token = _queryCts?.Token ?? CancellationToken.None;
        IsLoading = true;
        _progressTimer.Start();
        _ = Task.Run(() => ReadPageAsync(query, generation, token), token);
    }

    private async Task ReadPageAsync(IEventQuery query, int generation, CancellationToken token)
    {
        var stopwatch = Stopwatch.StartNew();
        var added = 0;
        try
        {
            while (added < PageSize && !token.IsCancellationRequested)
            {
                var chunk = query.ReadNext(Math.Min(ChunkSize, PageSize - added), token);
                if (chunk.Count == 0) break;
                added += chunk.Count;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (generation != _queryGeneration) return;
                    foreach (var item in chunk)
                        Events.Add(item);
                });
                if (generation != _queryGeneration) return;
            }
        }
        catch (Exception ex)
        {
            // A superseded query gets disposed under the reader; only a failure of the current one is worth reporting.
            if (generation != _queryGeneration) return;
            Log.Error(ex, "Reading events failed");
            await Dispatcher.UIThread.InvokeAsync(() => QueryError = ex.Message);
        }

        var exhausted = query.IsExhausted;
        var errors = query.Errors;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (generation != _queryGeneration) return;
            IsLoading = false;
            _progressTimer.Stop();
            ScannedCount = query.Scanned;
            CanLoadMore = !exhausted;
            if (errors.Count > 0)
                QueryError = string.Join(" · ", errors.Select(e => e.Error).Distinct());
            UpdateCounts();
            StatusText = exhausted
                ? $"{EventCountText} · {stopwatch.ElapsedMilliseconds} ms"
                : $"{EventCountText} · more available · {stopwatch.ElapsedMilliseconds} ms";
            OnPropertyChanged(nameof(IsEmpty));
            CompletePendingReveal();
            Log.Debug("Page of {Added} events in {Elapsed} ms, scanned {Scanned}, exhausted {Exhausted}", added, stopwatch.ElapsedMilliseconds, query.Scanned, exhausted);
        });
    }

    private void UpdateLoadingStatus()
    {
        if (_query is { } query)
        {
            ScannedCount = query.Scanned;
            StatusText = $"Reading… {EventCountText}, {ScannedCount:#,0} scanned";
        }
        UpdateCounts();
    }

    /// <summary>"1 event" / "1 234 events" for the status texts.</summary>
    public string EventCountText => $"{Events.Count:#,0} {(Events.Count == 1 ? "event" : "events")}";

    private void UpdateCounts()
    {
        var channels = SelectedChannelNames.Count;
        CountText = channels == 0
            ? $"{ChannelCount} channels"
            : $"{channels} {(channels == 1 ? "channel" : "channels")} · {EventCountText}{(LiveCount > 0 ? $" · {LiveCount} live" : string.Empty)}";
    }

    /// <summary>Stops a slow search and keeps what has been read so far.</summary>
    [RelayCommand]
    private void CancelLoading()
    {
        _queryCts?.Cancel();
    }

    // ----- Live tail -----

    [ObservableProperty]
    private bool _isLive;

    /// <summary>Events that arrived through the watcher since the query ran.</summary>
    [ObservableProperty]
    private int _liveCount;

    partial void OnIsLiveChanged(bool value)
    {
        if (value) StartWatcher();
        else StopWatcher();
        UpdateCounts();
    }

    [RelayCommand]
    private void ToggleLive() => IsLive = !IsLive;

    private void StartWatcher()
    {
        StopWatcher();
        if (!IsLive || SelectedChannelNames.Count == 0) return;

        var filter = BuildFilter();
        var generation = _queryGeneration;
        _watcher = _source.Watch(SelectedChannelNames, filter,
            item => Dispatcher.UIThread.Post(() =>
            {
                if (generation != _queryGeneration) return;
                Events.Insert(0, item);
                LiveCount++;
                while (Events.Count > MaxRows)
                    Events.RemoveAt(Events.Count - 1);
                UpdateCounts();
                OnPropertyChanged(nameof(IsEmpty));
            }),
            (channel, ex) => Log.Warning(ex, "Watching {Channel} failed", channel));
        Log.Information("Live tail on {Channels}", string.Join(",", SelectedChannelNames));
    }

    private void StopWatcher()
    {
        _watcher?.Dispose();
        _watcher = null;
    }

    // ----- Detail pane -----

    [ObservableProperty]
    private double _detailHeight;

    [ObservableProperty]
    private int _detailTab;

    [ObservableProperty]
    private string _detailXml = string.Empty;

    [ObservableProperty]
    private bool _isXmlLoading;

    partial void OnDetailHeightChanged(double value)
    {
        _settings.DetailHeight = value;
        _settings.Save();
    }

    partial void OnSelectedEventChanged(EventItem? value)
    {
        DetailXml = string.Empty;
        if (DetailTab == 2) _ = LoadXmlAsync(value);
    }

    partial void OnDetailTabChanged(int value)
    {
        if (value == 2 && DetailXml.Length == 0) _ = LoadXmlAsync(SelectedEvent);
    }

    private async Task LoadXmlAsync(EventItem? item)
    {
        if (item is null) return;
        IsXmlLoading = true;
        try
        {
            var xml = await Task.Run(() => _source.ReadXml(item.Channel, item.RecordId));
            if (SelectedEvent == item)
                DetailXml = PrettyXml(xml);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Reading XML of {Channel}#{RecordId} failed", item.Channel, item.RecordId);
            if (SelectedEvent == item)
                DetailXml = ex.Message;
        }
        finally
        {
            IsXmlLoading = false;
        }
    }

    private static string PrettyXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return string.Empty;
        try
        {
            return System.Xml.Linq.XDocument.Parse(xml).ToString();
        }
        catch (Exception)
        {
            return xml;
        }
    }

    [RelayCommand]
    private async Task CopyEvent(EventItem? item)
    {
        item ??= SelectedEvent;
        if (item is null || CopyToClipboard is null) return;
        await CopyToClipboard(item.ToClipboardText());
        StatusText = "Event copied";
    }

    [RelayCommand]
    private async Task CopyXml()
    {
        if (SelectedEvent is null || CopyToClipboard is null) return;
        if (DetailXml.Length == 0) await LoadXmlAsync(SelectedEvent);
        await CopyToClipboard(DetailXml);
        StatusText = "XML copied";
    }

    // ----- App -----

    /// <summary>The theme mode: System (follows Windows live), Light or Dark.</summary>
    public string ThemeMode => ThemeModes.Normalize(_settings.Theme);

    public string ThemeIcon => ThemeModes.Icon(_settings.Theme);

    public string ThemeTip => ThemeMode switch
    {
        ThemeModes.Light => "Theme: light — switch to dark (Ctrl+Shift+L)",
        ThemeModes.Dark => "Theme: dark — switch to system (Ctrl+Shift+L)",
        _ => "Theme: system, follows Windows — switch to light (Ctrl+Shift+L)",
    };

    /// <summary>Header switch and Ctrl+Shift+L: System → Light → Dark → System.</summary>
    [RelayCommand]
    private void CycleTheme() => SetTheme(ThemeModes.Next(_settings.Theme));

    [RelayCommand]
    private void SetTheme(string? mode)
    {
        mode = ThemeModes.Normalize(mode);
        _settings.Theme = mode == ThemeModes.System ? null : mode;
        _settings.ThemeVersion = ThemeModes.CurrentVersion;
        _settings.Save();
        ThemeModes.Apply(_settings.Theme);
        OnPropertyChanged(nameof(ThemeMode));
        OnPropertyChanged(nameof(ThemeIcon));
        OnPropertyChanged(nameof(ThemeTip));
        Log.Debug("Theme mode set to {Theme}", mode);
    }

    [RelayCommand]
    private void ToggleAppLog() => ShowAppLog = !ShowAppLog;

    // ----- Alerts -----

    public AlertsViewModel Alerts { get; }

    /// <summary>Toast cards currently on screen, newest last.</summary>
    public ObservableCollection<ToastViewModel> Toasts { get; } = new();

    /// <summary>Raised on every alert hit so the window can flash the taskbar button.</summary>
    public event Action<AlertHit>? AlertRaised;

    /// <summary>The Alerts page replaces the event view while on.</summary>
    [ObservableProperty]
    private bool _showAlerts;

    public bool ShowEvents => !ShowAppLog && !ShowAlerts;

    private (string Channel, long RecordId)? _pendingReveal;

    partial void OnShowAlertsChanged(bool value)
    {
        if (value) ShowAppLog = false;
        Alerts.IsVisible = value;
        OnPropertyChanged(nameof(ShowEvents));
    }

    partial void OnShowAppLogChanged(bool value)
    {
        if (value) ShowAlerts = false;
        OnPropertyChanged(nameof(ShowEvents));
    }

    [RelayCommand]
    private void ToggleAlerts() => ShowAlerts = !ShowAlerts;

    private void OnAlertTriggered(AlertHit hit)
    {
        if (hit.Rule.ShowToast)
        {
            var toast = new ToastViewModel(hit);
            Toasts.Add(toast);
            while (Toasts.Count > 4)
                Toasts.RemoveAt(0);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Toasts.Remove(toast);
            };
            timer.Start();
        }
        AlertRaised?.Invoke(hit);
    }

    [RelayCommand]
    private void DismissToast(ToastViewModel? toast)
    {
        if (toast is not null) Toasts.Remove(toast);
    }

    /// <summary>Jumps to the event behind a toast or a history row: its channel, all levels, and the row selected once loaded.</summary>
    [RelayCommand]
    private void RevealAlert(object? parameter)
    {
        var hit = parameter switch
        {
            ToastViewModel toast => toast.Hit,
            AlertHit h => h,
            _ => null,
        };
        if (hit is null) return;
        if (parameter is ToastViewModel t) Toasts.Remove(t);
        if (hit.Event.RecordId == 0) return; // a test hit has no event behind it

        ShowAlerts = false;
        ShowAppLog = false;
        _pendingReveal = (hit.Event.Channel, hit.Event.RecordId);
        foreach (var chip in Levels) chip.IsSelected = true;
        SearchText = string.Empty;
        EventIdText = string.Empty;
        ProviderText = string.Empty;
        SelectChannels([hit.Event.Channel]);
    }

    private void CompletePendingReveal()
    {
        if (_pendingReveal is not { } reveal) return;
        _pendingReveal = null;
        var match = Events.FirstOrDefault(e => e.Channel == reveal.Channel && e.RecordId == reveal.RecordId);
        if (match is not null)
            SelectedEvent = match;
        else
            StatusText = "The event is outside the loaded range";
    }


    /// <summary>Relaunches the same executable elevated so the Security log becomes readable.</summary>
    [RelayCommand]
    private void RestartAsAdministrator()
    {
        var exe = Environment.ProcessPath;
        if (exe is null) return;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                desktop.Shutdown();
        }
        catch (Exception ex)
        {
            // The UAC prompt was declined or elevation is blocked; nothing else to do.
            Log.Information("Elevation declined: {Message}", ex.Message);
            StatusText = "Elevation cancelled";
        }
    }

    public void Shutdown()
    {
        _alertService.Dispose();
        _queryCts?.Cancel();
        StopWatcher();
        _query?.Dispose();
        _query = null;
    }

    private static string DescribeUpdater(Updater updater) => updater.State switch
    {
        UpdateState.Disabled => $"updates off: {updater.DisabledReason}",
        UpdateState.Checking => "checking for updates…",
        UpdateState.UpToDate => $"up to date · {updater.Channel.ToString().ToLowerInvariant()} · {updater.LastCheck:HH:mm}",
        UpdateState.Failed => updater.Error ?? "update failed",
        _ => string.Empty,
    };
}
