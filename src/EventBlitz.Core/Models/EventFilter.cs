namespace EventBlitz.Core.Models;

/// <summary>
/// What the user asked to see. Levels, time range and event IDs become an XPath query the log service evaluates
/// (cheap, no message formatting); provider and text are matched here on the formatted event because the Event
/// Log's XPath dialect cannot search message text.
/// </summary>
public sealed record EventFilter
{
    public LevelMask Levels { get; init; } = LevelMask.All;

    /// <summary>Only events younger than this; null means the whole log.</summary>
    public TimeSpan? MaxAge { get; init; }

    public EventIdSpec EventIds { get; init; } = EventIdSpec.Empty;

    /// <summary>Case-insensitive substring of the provider name.</summary>
    public string? Provider { get; init; }

    /// <summary>Case-insensitive substring of message, provider, channel, event ID or user. Several words must all match.</summary>
    public string? Text { get; init; }

    public static readonly EventFilter Default = new();

    public bool HasClientSideFilter => !string.IsNullOrWhiteSpace(Provider) || !string.IsNullOrWhiteSpace(Text);

    /// <summary>XPath the Event Log evaluates; "*" when nothing is constrained.</summary>
    public string ToXPath()
    {
        var parts = new List<string>();

        if (Levels != LevelMask.All && Levels != LevelMask.None)
        {
            var levels = new List<string>();
            if (Levels.HasFlag(LevelMask.Critical)) levels.Add("Level=1");
            if (Levels.HasFlag(LevelMask.Error)) levels.Add("Level=2");
            if (Levels.HasFlag(LevelMask.Warning)) levels.Add("Level=3");
            if (Levels.HasFlag(LevelMask.Information)) { levels.Add("Level=4"); levels.Add("Level=0"); }
            if (Levels.HasFlag(LevelMask.Verbose)) levels.Add("Level=5");
            parts.Add("(" + string.Join(" or ", levels) + ")");
        }

        if (MaxAge is { } age)
            parts.Add($"TimeCreated[timediff(@SystemTime) <= {(long)age.TotalMilliseconds}]");

        if (EventIds.ToXPath() is { } ids)
            parts.Add(ids);

        return parts.Count == 0 ? "*" : $"*[System[{string.Join(" and ", parts)}]]";
    }

    /// <summary>The part of the filter XPath cannot express: provider and free text.</summary>
    public bool MatchesClientSide(EventItem item)
    {
        if (!string.IsNullOrWhiteSpace(Provider) && !item.Provider.Contains(Provider.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(Text))
        {
            foreach (var word in Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!item.SearchText.Contains(word, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        return true;
    }

    /// <summary>Full check used for live events that arrive outside the query (the watcher already applied the XPath).</summary>
    public bool Matches(EventItem item) =>
        Levels.Contains(item.Level)
        && (MaxAge is null || item.TimeCreated >= DateTime.Now - MaxAge.Value)
        && EventIds.Matches(item.EventId)
        && MatchesClientSide(item);
}
