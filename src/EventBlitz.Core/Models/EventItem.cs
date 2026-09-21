namespace EventBlitz.Core.Models;

/// <summary>
/// One event, detached from the Event Log API so it can live in a list after the reader moved on.
/// The XML view is read on demand through <see cref="Services.IEventLogSource.ReadXml"/>, not stored here.
/// </summary>
public sealed class EventItem
{
    public required string Channel { get; init; }
    public required long RecordId { get; init; }
    public required DateTime TimeCreated { get; init; }
    public required EventLevel Level { get; init; }
    public required int EventId { get; init; }
    public required string Provider { get; init; }
    public required string Message { get; init; }
    public string? Task { get; init; }
    public string? Opcode { get; init; }
    public string? Keywords { get; init; }
    public string? Computer { get; init; }
    public string? User { get; init; }
    public int? ProcessId { get; init; }
    public int? ThreadId { get; init; }
    public Guid? ActivityId { get; init; }

    public string LevelName => Level.DisplayName();

    /// <summary>First line of the message for the list; the full text lives in the detail pane.</summary>
    public string FirstLine
    {
        get
        {
            var span = Message.AsSpan().TrimStart();
            var end = span.IndexOfAny('\r', '\n');
            return (end < 0 ? span : span[..end]).ToString();
        }
    }

    /// <summary>Everything a text filter should look at, computed once.</summary>
    public string SearchText => _searchText ??= string.Join('\n', Channel, Provider, EventId.ToString(), Message, Task, Keywords, User, Computer);
    private string? _searchText;

    /// <summary>Plain text block for the clipboard.</summary>
    public string ToClipboardText() =>
        $"{TimeCreated:yyyy-MM-dd HH:mm:ss.fff} [{LevelName}] {Provider} · Event {EventId} · {Channel}{Environment.NewLine}{Message}";
}
