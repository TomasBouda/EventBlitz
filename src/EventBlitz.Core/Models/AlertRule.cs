namespace EventBlitz.Core.Models;

/// <summary>Which sound an alert plays; the system sounds need nothing on disk, a custom one is a WAV path.</summary>
public enum AlertSound
{
    None,
    Notify,
    Warning,
    Critical,
    Custom,
}

/// <summary>
/// A subscription: watch these channels for events matching the filter and notify. Serialised as-is into the user
/// settings, so everything here is a plain property.
/// </summary>
public sealed class AlertRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "New alert";

    public bool IsEnabled { get; set; } = true;

    /// <summary>Channel names; an empty list matches nothing (the rule is idle).</summary>
    public List<string> Channels { get; set; } = new();

    public LevelMask Levels { get; set; } = LevelMask.Critical | LevelMask.Error;

    /// <summary>Event ID list in the same syntax as the filter strip ("1000, 1001-1010, !4624"); empty = any.</summary>
    public string EventIds { get; set; } = string.Empty;

    /// <summary>Case-insensitive substring of the provider; empty = any.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Words that must all appear in the message (or provider, channel, ID, user); empty = any.</summary>
    public string Text { get; set; } = string.Empty;

    public AlertSound Sound { get; set; } = AlertSound.Notify;

    /// <summary>WAV file for <see cref="AlertSound.Custom"/>.</summary>
    public string? SoundPath { get; set; }

    /// <summary>Show the in-app toast (sound alone is possible too).</summary>
    public bool ShowToast { get; set; } = true;

    /// <summary>The filter the watcher evaluates; no time bound, the watcher only ever sees new events anyway.</summary>
    public EventFilter ToFilter()
    {
        EventIdSpec.TryParse(EventIds, out var ids, out _);
        return new EventFilter
        {
            Levels = Levels == LevelMask.None ? LevelMask.All : Levels,
            EventIds = ids ?? EventIdSpec.Empty,
            Provider = Provider,
            Text = Text,
        };
    }

    public bool Matches(EventItem item) =>
        Channels.Contains(item.Channel, StringComparer.OrdinalIgnoreCase) && ToFilter().Matches(item);

    /// <summary>"Application + System · errors · Fixture Service" for lists.</summary>
    public string Summary
    {
        get
        {
            var parts = new List<string>();
            parts.Add(Channels.Count switch
            {
                0 => "no channels",
                <= 2 => string.Join(" + ", Channels.Select(c => ChannelInfo.Describe(c).DisplayName)),
                _ => $"{Channels.Count} channels",
            });
            parts.Add(DescribeLevels(Levels));
            if (!string.IsNullOrWhiteSpace(EventIds)) parts.Add($"ID {EventIds.Trim()}");
            if (!string.IsNullOrWhiteSpace(Provider)) parts.Add(Provider.Trim());
            if (!string.IsNullOrWhiteSpace(Text)) parts.Add($"\"{Text.Trim()}\"");
            return string.Join(" · ", parts);
        }
    }

    private static string DescribeLevels(LevelMask mask)
    {
        if (mask == LevelMask.All || mask == LevelMask.None) return "all levels";
        var names = new List<string>();
        if (mask.HasFlag(LevelMask.Critical)) names.Add("critical");
        if (mask.HasFlag(LevelMask.Error)) names.Add("error");
        if (mask.HasFlag(LevelMask.Warning)) names.Add("warning");
        if (mask.HasFlag(LevelMask.Information)) names.Add("info");
        if (mask.HasFlag(LevelMask.Verbose)) names.Add("verbose");
        return string.Join("/", names);
    }

    public AlertRule Clone() => new()
    {
        Id = Id,
        Name = Name,
        IsEnabled = IsEnabled,
        Channels = new List<string>(Channels),
        Levels = Levels,
        EventIds = EventIds,
        Provider = Provider,
        Text = Text,
        Sound = Sound,
        SoundPath = SoundPath,
        ShowToast = ShowToast,
    };
}
