using EventBlitz.Core.Models;
using Xunit;

namespace EventBlitz.Core.Tests;

public class EventFilterTests
{
    private static EventItem Item(EventLevel level = EventLevel.Information, int id = 1, string provider = "Test", string message = "hello world", DateTime? time = null) =>
        new()
        {
            Channel = "Application",
            RecordId = 1,
            TimeCreated = time ?? DateTime.Now,
            Level = level,
            EventId = id,
            Provider = provider,
            Message = message,
        };

    [Fact]
    public void Default_filter_is_a_bare_star()
    {
        Assert.Equal("*", EventFilter.Default.ToXPath());
    }

    [Fact]
    public void Levels_map_to_level_predicates_and_information_includes_log_always()
    {
        var filter = new EventFilter { Levels = LevelMask.Error | LevelMask.Information };
        Assert.Equal("*[System[(Level=2 or Level=4 or Level=0)]]", filter.ToXPath());
    }

    [Fact]
    public void Max_age_uses_timediff_in_milliseconds()
    {
        var filter = new EventFilter { MaxAge = TimeSpan.FromHours(1) };
        Assert.Equal("*[System[TimeCreated[timediff(@SystemTime) <= 3600000]]]", filter.ToXPath());
    }

    [Fact]
    public void Everything_combined_is_and_ed()
    {
        Assert.True(EventIdSpec.TryParse("1000-1002, !1001", out var ids, out _));
        var filter = new EventFilter { Levels = LevelMask.Critical, MaxAge = TimeSpan.FromMinutes(1), EventIds = ids! };
        Assert.Equal("*[System[(Level=1) and TimeCreated[timediff(@SystemTime) <= 60000] and ((EventID>=1000 and EventID<=1002)) and not(EventID=1001)]]", filter.ToXPath());
    }

    [Fact]
    public void Text_filter_requires_every_word_case_insensitively()
    {
        var filter = new EventFilter { Text = "HELLO world" };
        Assert.True(filter.MatchesClientSide(Item()));
        Assert.False(filter.MatchesClientSide(Item(message: "hello there")));
    }

    [Fact]
    public void Text_filter_also_looks_at_provider_and_event_id()
    {
        Assert.True(new EventFilter { Text = "kernel" }.MatchesClientSide(Item(provider: "Microsoft-Windows-Kernel-Power")));
        Assert.True(new EventFilter { Text = "4624" }.MatchesClientSide(Item(id: 4624)));
    }

    [Fact]
    public void Provider_filter_is_a_substring()
    {
        Assert.True(new EventFilter { Provider = "kernel" }.MatchesClientSide(Item(provider: "Microsoft-Windows-Kernel-Power")));
        Assert.False(new EventFilter { Provider = "disk" }.MatchesClientSide(Item(provider: "Microsoft-Windows-Kernel-Power")));
    }

    [Fact]
    public void Full_match_applies_levels_age_and_ids_for_live_events()
    {
        Assert.True(EventIdSpec.TryParse("!5", out var ids, out _));
        var filter = new EventFilter { Levels = LevelMask.Error, MaxAge = TimeSpan.FromHours(1), EventIds = ids! };
        Assert.True(filter.Matches(Item(EventLevel.Error, id: 1)));
        Assert.False(filter.Matches(Item(EventLevel.Warning, id: 1)));
        Assert.False(filter.Matches(Item(EventLevel.Error, id: 5)));
        Assert.False(filter.Matches(Item(EventLevel.Error, id: 1, time: DateTime.Now.AddHours(-2))));
    }

    [Fact]
    public void Log_always_counts_as_information()
    {
        Assert.True(LevelMask.Information.Contains(EventLevel.LogAlways));
        Assert.Equal("Information", EventLevel.LogAlways.DisplayName());
    }
}
