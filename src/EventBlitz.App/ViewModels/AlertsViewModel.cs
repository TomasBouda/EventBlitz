using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EventBlitz.App.Services;
using EventBlitz.Core.Models;

namespace EventBlitz.App.ViewModels;

/// <summary>Editable form of one alert rule; every change is pushed back to the settings and the watchers.</summary>
public sealed partial class AlertRuleViewModel : ObservableObject
{
    private readonly AlertRule _rule;
    private bool _loading = true;

    public AlertRuleViewModel(AlertRule rule)
    {
        _rule = rule;
        _name = rule.Name;
        _isEnabled = rule.IsEnabled;
        _channelsText = string.Join(", ", rule.Channels);
        _eventIds = rule.EventIds;
        _provider = rule.Provider;
        _text = rule.Text;
        _sound = rule.Sound;
        _soundPath = rule.SoundPath ?? string.Empty;
        _showToast = rule.ShowToast;
        Levels = LevelChip.All();
        foreach (var chip in Levels)
        {
            chip.IsSelected = rule.Levels.HasFlag(chip.Mask);
            chip.PropertyChanged += (_, _) => OnEdited();
        }
        _loading = false;
    }

    public Guid Id => _rule.Id;

    public LevelChip[] Levels { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _channelsText;
    [ObservableProperty] private string _eventIds;
    [ObservableProperty] private string _provider;
    [ObservableProperty] private string _text;
    [ObservableProperty] private AlertSound _sound;
    [ObservableProperty] private string _soundPath;
    [ObservableProperty] private bool _showToast;

    /// <summary>Why the event ID text cannot be used, or null.</summary>
    [ObservableProperty] private string? _eventIdError;

    public bool IsCustomSound => Sound == AlertSound.Custom;

    public bool HasSound => Sound != AlertSound.None;

    public string Summary => ToRule().Summary;

    /// <summary>Raised after any edit; the owner saves and re-arms.</summary>
    public event Action? Edited;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading || e.PropertyName is nameof(Summary) or nameof(IsCustomSound) or nameof(HasSound) or nameof(EventIdError)) return;
        if (e.PropertyName == nameof(Sound))
        {
            OnPropertyChanged(nameof(IsCustomSound));
            OnPropertyChanged(nameof(HasSound));
        }
        if (e.PropertyName == nameof(EventIds))
            EventIdError = EventIdSpec.TryParse(EventIds, out _, out var bad) ? null : $"Not an event ID: {bad}";
        OnEdited();
    }

    private void OnEdited()
    {
        if (_loading) return;
        OnPropertyChanged(nameof(Summary));
        Edited?.Invoke();
    }

    /// <summary>Replaces the channel list with the given names (the "use selected channels" button).</summary>
    public void SetChannels(IEnumerable<string> channels) => ChannelsText = string.Join(", ", channels);

    public AlertRule ToRule()
    {
        _rule.Name = string.IsNullOrWhiteSpace(Name) ? "Alert" : Name.Trim();
        _rule.IsEnabled = IsEnabled;
        _rule.Channels = ChannelsText.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
        _rule.Levels = Levels.Where(l => l.IsSelected).Aggregate(LevelMask.None, (m, l) => m | l.Mask);
        _rule.EventIds = EventIds.Trim();
        _rule.Provider = Provider.Trim();
        _rule.Text = Text.Trim();
        _rule.Sound = Sound;
        _rule.SoundPath = string.IsNullOrWhiteSpace(SoundPath) ? null : SoundPath.Trim();
        _rule.ShowToast = ShowToast;
        return _rule;
    }
}

/// <summary>Alerts page: the rules, the editor of the selected one and the hits so far.</summary>
public sealed partial class AlertsViewModel : ObservableObject
{
    private readonly AlertService _service;
    private readonly UserSettings _settings;
    private readonly DispatcherTimer _applyDebounce;

    public AlertsViewModel(AlertService service, UserSettings settings)
    {
        _service = service;
        _settings = settings;
        _isMuted = settings.AlertsMuted;
        service.IsMuted = _isMuted;

        _applyDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _applyDebounce.Tick += (_, _) =>
        {
            _applyDebounce.Stop();
            Save();
        };

        foreach (var rule in settings.Alerts)
            Rules.Add(Wrap(rule));
        SelectedRule = Rules.FirstOrDefault();
        service.Apply(settings.Alerts);
        service.Triggered += _ => { if (!IsVisible) UnreadCount++; };
    }

    public ObservableCollection<AlertRuleViewModel> Rules { get; } = new();

    public ObservableCollection<AlertHit> History => _service.History;

    public AlertSound[] SoundOptions { get; } = Enum.GetValues<AlertSound>();

    [ObservableProperty] private AlertRuleViewModel? _selectedRule;

    [ObservableProperty] private bool _isMuted;

    /// <summary>Hits since the page was last on screen; the header badge.</summary>
    [ObservableProperty] private int _unreadCount;

    /// <summary>Set by the window model while the page is shown, so hits do not count as unread.</summary>
    [ObservableProperty] private bool _isVisible;

    public bool HasRules => Rules.Count > 0;

    public int ActiveCount => Rules.Count(r => r.IsEnabled);

    /// <summary>Set by the window model: the channels and filter currently on screen, for "new alert from this view".</summary>
    public Func<(IReadOnlyList<string> Channels, EventFilter Filter)>? CurrentView { get; set; }

    partial void OnIsMutedChanged(bool value)
    {
        _service.IsMuted = value;
        _settings.AlertsMuted = value;
        _settings.Save();
    }

    partial void OnIsVisibleChanged(bool value)
    {
        if (value) UnreadCount = 0;
    }

    private AlertRuleViewModel Wrap(AlertRule rule)
    {
        var vm = new AlertRuleViewModel(rule);
        vm.Edited += () =>
        {
            _applyDebounce.Stop();
            _applyDebounce.Start();
            OnPropertyChanged(nameof(ActiveCount));
        };
        return vm;
    }

    private void Save()
    {
        _settings.Alerts = Rules.Select(r => r.ToRule()).ToList();
        _settings.Save();
        _service.Apply(_settings.Alerts);
        OnPropertyChanged(nameof(HasRules));
        OnPropertyChanged(nameof(ActiveCount));
    }

    [RelayCommand]
    private void Add()
    {
        var rule = new AlertRule();
        if (CurrentView?.Invoke() is { } view && view.Channels.Count > 0)
            rule.Channels = view.Channels.ToList();
        AddRule(rule);
    }

    /// <summary>A rule pre-filled with the channels and filter on screen: the fastest way to subscribe to what you are looking at.</summary>
    [RelayCommand]
    private void AddFromView()
    {
        var rule = new AlertRule();
        if (CurrentView?.Invoke() is { } view)
        {
            rule.Channels = view.Channels.ToList();
            rule.Levels = view.Filter.Levels;
            rule.EventIds = view.Filter.EventIds.IsEmpty ? string.Empty : DescribeIds(view.Filter.EventIds);
            rule.Provider = view.Filter.Provider ?? string.Empty;
            rule.Text = view.Filter.Text ?? string.Empty;
            rule.Name = rule.Channels.Count switch
            {
                0 => "New alert",
                1 => ChannelInfo.Describe(rule.Channels[0]).DisplayName,
                _ => string.Join(" + ", rule.Channels.Take(2).Select(c => ChannelInfo.Describe(c).DisplayName)),
            };
            if (!string.IsNullOrWhiteSpace(rule.Provider)) rule.Name += " · " + rule.Provider;
            if (!string.IsNullOrWhiteSpace(rule.Text)) rule.Name += " · " + rule.Text;
        }
        AddRule(rule);
    }

    private void AddRule(AlertRule rule)
    {
        var vm = Wrap(rule);
        Rules.Add(vm);
        SelectedRule = vm;
        Save();
    }

    [RelayCommand]
    private void Remove(AlertRuleViewModel? rule)
    {
        rule ??= SelectedRule;
        if (rule is null) return;
        var index = Rules.IndexOf(rule);
        Rules.Remove(rule);
        SelectedRule = Rules.Count == 0 ? null : Rules[Math.Clamp(index, 0, Rules.Count - 1)];
        Save();
    }

    [RelayCommand]
    private void Test(AlertRuleViewModel? rule)
    {
        rule ??= SelectedRule;
        if (rule is null) return;
        _service.Test(rule.ToRule());
    }

    [RelayCommand]
    private void ToggleMute() => IsMuted = !IsMuted;

    [RelayCommand]
    private void ClearHistory()
    {
        History.Clear();
        UnreadCount = 0;
    }

    private static string DescribeIds(EventIdSpec spec)
    {
        var parts = spec.Includes.Select(r => r.From == r.To ? r.From.ToString() : $"{r.From}-{r.To}")
            .Concat(spec.Excludes.Select(r => r.From == r.To ? $"!{r.From}" : $"!{r.From}-{r.To}"));
        return string.Join(", ", parts);
    }
}

/// <summary>A toast card in the corner of the window; disappears by itself or on click.</summary>
public sealed partial class ToastViewModel : ObservableObject
{
    public ToastViewModel(AlertHit hit)
    {
        Hit = hit;
    }

    public AlertHit Hit { get; }

    public string Title => Hit.Rule.Name;
    public EventLevel Level => Hit.Event.Level;
    public string LevelName => Hit.Event.LevelName;
    public string Subtitle => $"{Hit.Event.Provider} · ID {Hit.Event.EventId} · {ChannelInfo.Describe(Hit.Event.Channel).DisplayName}";
    public string Line => Hit.Event.FirstLine;
    public string TimeText => Hit.Time.ToString("HH:mm:ss");
}
