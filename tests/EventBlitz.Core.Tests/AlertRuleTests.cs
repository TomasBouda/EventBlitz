using EventBlitz.Core.Models;
using Xunit;

namespace EventBlitz.Core.Tests;

public class AlertRuleTests
{
    private static EventItem Item(string channel = "Application", EventLevel level = EventLevel.Error, int id = 1000, string provider = "Fixture Service", string message = "Database connection failed") =>
        new()
        {
            Channel = channel,
            RecordId = 1,
            TimeCreated = DateTime.Now,
            Level = level,
            EventId = id,
            Provider = provider,
            Message = message,
        };

    [Fact]
    public void Default_rule_matches_errors_and_criticals_on_its_channels_only()
    {
        var rule = new AlertRule { Channels = ["Application"] };
        Assert.True(rule.Matches(Item()));
        Assert.True(rule.Matches(Item(level: EventLevel.Critical)));
        Assert.False(rule.Matches(Item(level: EventLevel.Warning)));
        Assert.False(rule.Matches(Item(channel: "System")));
    }

    [Fact]
    public void Channel_comparison_ignores_case()
    {
        var rule = new AlertRule { Channels = ["application"] };
        Assert.True(rule.Matches(Item()));
    }

    [Fact]
    public void Ids_provider_and_text_narrow_the_rule()
    {
        var rule = new AlertRule { Channels = ["Application"], EventIds = "1000-1010", Provider = "fixture", Text = "database" };
        Assert.True(rule.Matches(Item()));
        Assert.False(rule.Matches(Item(id: 2000)));
        Assert.False(rule.Matches(Item(provider: "Other")));
        Assert.False(rule.Matches(Item(message: "all fine")));
    }

    [Fact]
    public void No_levels_means_every_level()
    {
        var rule = new AlertRule { Channels = ["Application"], Levels = LevelMask.None };
        Assert.True(rule.Matches(Item(level: EventLevel.Verbose)));
        Assert.Equal("*", rule.ToFilter().ToXPath());
    }

    [Fact]
    public void Summary_reads_naturally()
    {
        var rule = new AlertRule { Channels = ["Application", "System"], EventIds = "41", Provider = "Kernel-Power" };
        Assert.Equal("Application + System · critical/error · ID 41 · Kernel-Power", rule.Summary);
        Assert.Equal("no channels · critical/error", new AlertRule().Summary);
    }

    [Fact]
    public void Clone_is_independent()
    {
        var rule = new AlertRule { Channels = ["Application"] };
        var copy = rule.Clone();
        copy.Channels.Add("System");
        copy.Name = "changed";
        Assert.Single(rule.Channels);
        Assert.Equal("New alert", rule.Name);
        Assert.Equal(rule.Id, copy.Id);
    }
}
