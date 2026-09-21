using EventBlitz.Core.Models;
using EventBlitz.Core.Services;
using Xunit;

namespace EventBlitz.Core.Tests;

public class MergedEventQueryTests
{
    private static EventItem Item(string channel, int minutesAgo, string message = "m") => new()
    {
        Channel = channel,
        RecordId = minutesAgo,
        TimeCreated = new DateTime(2026, 1, 1, 12, 0, 0).AddMinutes(-minutesAgo),
        Level = EventLevel.Information,
        EventId = 1,
        Provider = "P",
        Message = message,
    };

    private sealed class ListCursor(string channel, params EventItem[] items) : MergedEventQuery.ICursor
    {
        private int _index;
        public string Channel { get; } = channel;
        public EventItem? TryReadNext() => _index < items.Length ? items[_index++] : null;
        public void Dispose() { }
    }

    private sealed class FailingCursor : MergedEventQuery.ICursor
    {
        public string Channel => "Security";
        public EventItem? TryReadNext() => throw new InvalidOperationException("access denied");
        public void Dispose() { }
    }

    [Fact]
    public void Merges_channels_newest_first()
    {
        using var query = new MergedEventQuery(EventFilter.Default,
        [
            () => new ListCursor("A", Item("A", 1), Item("A", 5), Item("A", 9)),
            () => new ListCursor("B", Item("B", 2), Item("B", 3), Item("B", 10)),
        ]);

        var page = query.ReadNext(4, CancellationToken.None);
        Assert.Equal(["A", "B", "B", "A"], page.Select(e => e.Channel));
        Assert.False(query.IsExhausted);

        var rest = query.ReadNext(10, CancellationToken.None);
        Assert.Equal([9L, 10L], rest.Select(e => e.RecordId));
        Assert.True(query.IsExhausted);
        Assert.Equal(6, query.Scanned);
    }

    [Fact]
    public void Client_side_filter_skips_events_but_still_counts_them_as_scanned()
    {
        using var query = new MergedEventQuery(new EventFilter { Text = "keep" },
        [
            () => new ListCursor("A", Item("A", 1, "drop"), Item("A", 2, "keep me"), Item("A", 3, "drop"), Item("A", 4, "KEEP")),
        ]);

        var page = query.ReadNext(10, CancellationToken.None);
        Assert.Equal([2L, 4L], page.Select(e => e.RecordId));
        Assert.Equal(4, query.Scanned);
    }

    [Fact]
    public void A_channel_that_cannot_be_opened_is_reported_and_the_others_still_work()
    {
        using var query = new MergedEventQuery(EventFilter.Default,
        [
            () => throw new InvalidOperationException("Security: access denied"),
            () => new ListCursor("A", Item("A", 1)),
        ]);

        Assert.Single(query.ReadNext(10, CancellationToken.None));
        Assert.Single(query.Errors);
        Assert.Contains("access denied", query.Errors[0].Error);
    }

    [Fact]
    public void A_cursor_failing_mid_read_is_reported_and_treated_as_exhausted()
    {
        using var query = new MergedEventQuery(EventFilter.Default,
        [
            () => new FailingCursor(),
            () => new ListCursor("A", Item("A", 1)),
        ]);

        Assert.Single(query.ReadNext(10, CancellationToken.None));
        Assert.True(query.IsExhausted);
        Assert.Equal("Security", query.Errors[0].Channel);
    }

    [Fact]
    public void Cancellation_ends_the_query()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var query = new MergedEventQuery(EventFilter.Default, [() => new ListCursor("A", Item("A", 1), Item("A", 2))]);
        Assert.Empty(query.ReadNext(10, cts.Token));
        Assert.True(query.IsExhausted);
    }
}
