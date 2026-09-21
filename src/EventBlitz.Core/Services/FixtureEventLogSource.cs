using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using EventBlitz.Core.Models;

namespace EventBlitz.Core.Services;

/// <summary>
/// Synthetic events from a JSON file (<c>tests/ui-fixture/events.json</c>), so scripted UI checks render the same
/// picture on every machine and never show real log content. Selected when the data folder contains the file.
/// </summary>
public sealed class FixtureEventLogSource : IEventLogSource
{
    public const string FileName = "events.json";

    private readonly List<ChannelInfo> _channels;
    private readonly List<EventItem> _events;

    private FixtureEventLogSource(List<ChannelInfo> channels, List<EventItem> events)
    {
        _channels = channels;
        _events = events;
    }

    public static FixtureEventLogSource Load(string path)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var file = JsonSerializer.Deserialize<FixtureFile>(File.ReadAllText(path), options)
                   ?? throw new InvalidDataException($"{path} is not a fixture file");

        var events = file.Events.Select((e, i) => new EventItem
        {
            Channel = e.Channel,
            RecordId = e.RecordId ?? i + 1,
            TimeCreated = e.Time,
            Level = e.Level,
            EventId = e.EventId,
            Provider = e.Provider,
            Message = e.Message,
            Task = e.Task,
            Opcode = e.Opcode,
            Keywords = e.Keywords,
            Computer = e.Computer ?? "FIXTURE-PC",
            User = e.User,
            ProcessId = e.ProcessId,
            ThreadId = e.ThreadId,
        }).ToList();

        var channels = file.Channels.Select(c =>
        {
            var (group, folder, display) = ChannelInfo.Describe(c.Name);
            var own = events.Where(e => e.Channel == c.Name).ToList();
            return new ChannelInfo
            {
                Name = c.Name,
                DisplayName = display,
                Group = group,
                Folder = folder,
                IsEnabled = c.Enabled ?? true,
                RecordCount = c.RecordCount ?? own.Count,
                FileSize = c.FileSize ?? own.Count * 1536L,
                LastWritten = own.Count == 0 ? null : own.Max(e => e.TimeCreated),
                AccessError = c.AccessError,
            };
        }).ToList();

        return new FixtureEventLogSource(channels, events);
    }

    public IReadOnlyList<ChannelInfo> ListChannels(Action<int, int>? progress = null)
    {
        progress?.Invoke(_channels.Count, _channels.Count);
        return _channels;
    }

    public IEventQuery Query(IReadOnlyList<string> channels, EventFilter filter) =>
        new MergedEventQuery(filter, channels.Select(channel => (Func<MergedEventQuery.ICursor>)(() =>
        {
            var info = _channels.FirstOrDefault(c => c.Name == channel);
            if (info?.AccessError is { } error)
                throw new InvalidOperationException($"{channel}: {error}");
            return new ListCursor(channel, _events
                .Where(e => e.Channel == channel && filter.Levels.Contains(e.Level) && filter.EventIds.Matches(e.EventId)
                            && (filter.MaxAge is null || e.TimeCreated >= DateTime.Now - filter.MaxAge.Value))
                .OrderByDescending(e => e.TimeCreated));
        })));

    /// <summary>The fixture never changes, so there is nothing to watch.</summary>
    public IDisposable Watch(IReadOnlyList<string> channels, EventFilter filter, Action<EventItem> onEvent, Action<string, Exception>? onError = null) =>
        new NoopDisposable();

    public string ReadXml(string channel, long recordId)
    {
        var item = _events.FirstOrDefault(e => e.Channel == channel && e.RecordId == recordId);
        if (item is null) return string.Empty;

        var xml = new XElement("Event",
            new XElement("System",
                new XElement("Provider", new XAttribute("Name", item.Provider)),
                new XElement("EventID", item.EventId),
                new XElement("Level", (int)item.Level),
                new XElement("TimeCreated", new XAttribute("SystemTime", item.TimeCreated.ToUniversalTime().ToString("o"))),
                new XElement("EventRecordID", item.RecordId),
                new XElement("Channel", item.Channel),
                new XElement("Computer", item.Computer ?? string.Empty)),
            new XElement("EventData", new XElement("Data", item.Message)));
        return xml.ToString();
    }

    private sealed class ListCursor(string channel, IEnumerable<EventItem> items) : MergedEventQuery.ICursor
    {
        private readonly IEnumerator<EventItem> _enumerator = items.GetEnumerator();

        public string Channel { get; } = channel;

        public EventItem? TryReadNext() => _enumerator.MoveNext() ? _enumerator.Current : null;

        public void Dispose() => _enumerator.Dispose();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class FixtureFile
    {
        public List<FixtureChannel> Channels { get; set; } = new();
        public List<FixtureEvent> Events { get; set; } = new();
    }

    private sealed class FixtureChannel
    {
        public string Name { get; set; } = string.Empty;
        public bool? Enabled { get; set; }
        public long? RecordCount { get; set; }
        public long? FileSize { get; set; }
        public string? AccessError { get; set; }
    }

    private sealed class FixtureEvent
    {
        public string Channel { get; set; } = string.Empty;
        public long? RecordId { get; set; }
        public DateTime Time { get; set; }
        public EventLevel Level { get; set; } = EventLevel.Information;
        public int EventId { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Task { get; set; }
        public string? Opcode { get; set; }
        public string? Keywords { get; set; }
        public string? Computer { get; set; }
        public string? User { get; set; }
        public int? ProcessId { get; set; }
        public int? ThreadId { get; set; }
    }
}
