using EventBlitz.Core.Models;

namespace EventBlitz.Core.Services;

/// <summary>
/// Where events come from: the live Windows Event Log, or a JSON fixture for scripted UI checks.
/// Every call is synchronous and may take a while; callers run them off the UI thread.
/// </summary>
public interface IEventLogSource
{
    /// <summary>All channels of the machine with their size and record count. Slow (hundreds of channels), call once.</summary>
    IReadOnlyList<ChannelInfo> ListChannels(Action<int, int>? progress = null);

    /// <summary>Opens a query over the channels, newest events first, merged in time order.</summary>
    IEventQuery Query(IReadOnlyList<string> channels, EventFilter filter);

    /// <summary>Subscribes to new events on the channels; the callback runs on a thread-pool thread.</summary>
    IDisposable Watch(IReadOnlyList<string> channels, EventFilter filter, Action<EventItem> onEvent, Action<string, Exception>? onError = null);

    /// <summary>The event's XML as the log stores it, for the details pane.</summary>
    string ReadXml(string channel, long recordId);
}

/// <summary>A forward-only cursor over the matching events of one or more channels, newest first.</summary>
public interface IEventQuery : IDisposable
{
    /// <summary>True once every channel has been read to its end.</summary>
    bool IsExhausted { get; }

    /// <summary>Records the Event Log handed over so far, matching or not — feedback for a slow text search.</summary>
    long Scanned { get; }

    /// <summary>Channels that could not be opened, with the reason (typically "access denied" on Security).</summary>
    IReadOnlyList<(string Channel, string Error)> Errors { get; }

    /// <summary>Reads up to <paramref name="count"/> matching events. Fewer means the query is exhausted or was cancelled.</summary>
    IReadOnlyList<EventItem> ReadNext(int count, CancellationToken cancellationToken);
}
