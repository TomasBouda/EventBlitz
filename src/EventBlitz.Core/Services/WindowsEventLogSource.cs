using System.Collections.Concurrent;
using System.Diagnostics.Eventing.Reader;
using System.Security.Principal;
using EventBlitz.Core.Models;
using EventLevel = EventBlitz.Core.Models.EventLevel;

namespace EventBlitz.Core.Services;

/// <summary>The real thing: the local machine's Event Log through <c>System.Diagnostics.Eventing.Reader</c>.</summary>
public sealed class WindowsEventLogSource : IEventLogSource
{
    private static readonly ConcurrentDictionary<string, string> AccountNames = new();

    public IReadOnlyList<ChannelInfo> ListChannels(Action<int, int>? progress = null)
    {
        var session = EventLogSession.GlobalSession;
        var names = session.GetLogNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        var result = new List<ChannelInfo>(names.Count);

        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            var (group, folder, display) = ChannelInfo.Describe(name);
            long? records = null, size = null;
            DateTime? lastWritten = null;
            string? error = null;
            var enabled = true;

            try
            {
                var info = session.GetLogInformation(name, PathType.LogName);
                records = info.RecordCount;
                size = info.FileSize;
                lastWritten = info.LastWriteTime;
            }
            catch (UnauthorizedAccessException)
            {
                error = "Access denied: run as administrator to read this log";
            }
            catch (EventLogException ex)
            {
                error = ex.Message;
            }

            try
            {
                using var config = new EventLogConfiguration(name, session);
                enabled = config.IsEnabled;
            }
            catch (Exception)
            {
                // Some channels have no readable configuration; treat them as enabled.
            }

            result.Add(new ChannelInfo
            {
                Name = name,
                DisplayName = display,
                Group = group,
                Folder = folder,
                IsEnabled = enabled,
                RecordCount = records,
                FileSize = size,
                LastWritten = lastWritten,
                AccessError = error,
            });
            progress?.Invoke(i + 1, names.Count);
        }

        return result;
    }

    public IEventQuery Query(IReadOnlyList<string> channels, EventFilter filter)
    {
        var xpath = filter.ToXPath();
        return new MergedEventQuery(filter, channels.Select(channel => (Func<MergedEventQuery.ICursor>)(() => new ReaderCursor(channel, xpath))));
    }

    public IDisposable Watch(IReadOnlyList<string> channels, EventFilter filter, Action<EventItem> onEvent, Action<string, Exception>? onError = null)
    {
        var xpath = filter.ToXPath();
        var watchers = new List<EventLogWatcher>();
        foreach (var channel in channels)
        {
            try
            {
                var watcher = new EventLogWatcher(new EventLogQuery(channel, PathType.LogName, xpath));
                var captured = channel;
                watcher.EventRecordWritten += (_, e) =>
                {
                    if (e.EventException is not null)
                    {
                        onError?.Invoke(captured, e.EventException);
                        return;
                    }
                    if (e.EventRecord is null) return;
                    using var record = e.EventRecord;
                    var item = Convert(record, captured);
                    if (filter.MatchesClientSide(item))
                        onEvent(item);
                };
                watcher.Enabled = true;
                watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                onError?.Invoke(channel, ex);
            }
        }
        return new WatcherGroup(watchers);
    }

    public string ReadXml(string channel, long recordId)
    {
        using var reader = new EventLogReader(new EventLogQuery(channel, PathType.LogName, $"*[System[EventRecordID={recordId}]]"));
        using var record = reader.ReadEvent();
        return record?.ToXml() ?? string.Empty;
    }

    /// <summary>Detaches everything the UI needs from the record so it can be disposed right away.</summary>
    internal static EventItem Convert(EventRecord record, string channel)
    {
        var provider = record.ProviderName ?? "?";
        var level = record.Level is { } l && Enum.IsDefined(typeof(EventLevel), (int)l) ? (EventLevel)l : EventLevel.Information;

        return new EventItem
        {
            Channel = channel,
            RecordId = record.RecordId ?? 0,
            TimeCreated = record.TimeCreated ?? DateTime.MinValue,
            Level = level,
            EventId = record.Id,
            Provider = provider,
            Message = FormatMessage(record, provider),
            Task = Try(() => record.TaskDisplayName),
            Opcode = Try(() => record.OpcodeDisplayName),
            Keywords = Try(() => string.Join(", ", record.KeywordsDisplayNames)),
            Computer = record.MachineName,
            User = record.UserId is { } sid ? ResolveAccount(sid) : null,
            ProcessId = record.ProcessId,
            ThreadId = record.ThreadId,
            ActivityId = record.ActivityId,
        };
    }

    /// <summary>
    /// The rendered description, or, when the provider's message resources are missing (the classic viewer's
    /// "The description for Event ID … cannot be found" case), the raw event data, which is usually what matters anyway.
    /// </summary>
    private static string FormatMessage(EventRecord record, string provider)
    {
        try
        {
            var text = record.FormatDescription();
            if (!string.IsNullOrWhiteSpace(text))
                return text.TrimEnd();
        }
        catch (EventLogException)
        {
            // Fall through to the raw properties.
        }

        var values = new List<string>();
        try
        {
            foreach (var property in record.Properties)
            {
                var value = property.Value switch
                {
                    null => "null",
                    byte[] bytes => System.Convert.ToHexString(bytes),
                    string s => s,
                    _ => property.Value.ToString() ?? string.Empty,
                };
                values.Add(value);
            }
        }
        catch (EventLogException)
        {
            // Nothing readable; the header line still identifies the event.
        }

        return values.Count == 0
            ? $"Event {record.Id} from {provider} (no description available)"
            : $"Event {record.Id} from {provider} (description not available, raw data):{Environment.NewLine}{string.Join(Environment.NewLine, values)}";
    }

    private static string ResolveAccount(SecurityIdentifier sid) =>
        AccountNames.GetOrAdd(sid.Value, static (_, s) =>
        {
            try
            {
                return s.Translate(typeof(NTAccount)).Value;
            }
            catch (Exception)
            {
                return s.Value;
            }
        }, sid);

    private static string? Try(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (EventLogException)
        {
            return null;
        }
    }

    /// <summary>One channel read newest-first; the Event Log evaluates the XPath before handing records over.</summary>
    private sealed class ReaderCursor : MergedEventQuery.ICursor
    {
        private readonly EventLogReader _reader;

        public string Channel { get; }

        public ReaderCursor(string channel, string xpath)
        {
            Channel = channel;
            try
            {
                _reader = new EventLogReader(new EventLogQuery(channel, PathType.LogName, xpath) { ReverseDirection = true });
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"{channel}: access denied, run as administrator to read this log");
            }
            catch (EventLogException ex)
            {
                throw new InvalidOperationException($"{channel}: {ex.Message}");
            }
        }

        public EventItem? TryReadNext()
        {
            try
            {
                using var record = _reader.ReadEvent();
                return record is null ? null : Convert(record, Channel);
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException($"{Channel}: access denied, run as administrator to read this log");
            }
            catch (EventLogException ex)
            {
                throw new InvalidOperationException($"{Channel}: {ex.Message}");
            }
        }

        public void Dispose() => _reader.Dispose();
    }

    private sealed class WatcherGroup(List<EventLogWatcher> watchers) : IDisposable
    {
        public void Dispose()
        {
            foreach (var watcher in watchers)
            {
                try
                {
                    watcher.Enabled = false;
                    watcher.Dispose();
                }
                catch (Exception)
                {
                    // Disposing a watcher whose channel vanished must not break shutdown.
                }
            }
        }
    }
}
