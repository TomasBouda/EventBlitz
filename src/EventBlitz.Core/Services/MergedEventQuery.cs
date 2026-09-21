using EventBlitz.Core.Models;

namespace EventBlitz.Core.Services;

/// <summary>
/// K-way merge of per-channel cursors: each cursor peeks its next matching event and the newest one wins,
/// so a combined "Application + System" view stays in time order without loading everything first.
/// </summary>
public sealed class MergedEventQuery : IEventQuery
{
    /// <summary>A channel's forward-only reader; <see cref="TryReadNext"/> returns the next raw event, filtered or not.</summary>
    public interface ICursor : IDisposable
    {
        string Channel { get; }
        EventItem? TryReadNext();
    }

    private readonly List<(ICursor Cursor, EventItem? Peek)> _cursors = new();
    private readonly List<(string, string)> _errors = new();
    private readonly EventFilter _filter;
    private long _scanned;
    private bool _cancelled;
    private bool _primed;

    public MergedEventQuery(EventFilter filter, IEnumerable<Func<ICursor>> openCursors)
    {
        _filter = filter;
        foreach (var open in openCursors)
        {
            ICursor cursor;
            try
            {
                cursor = open();
            }
            catch (Exception ex)
            {
                _errors.Add(("?", ex.Message));
                continue;
            }
            _cursors.Add((cursor, null));
        }
    }

    public bool IsExhausted => _cancelled || (_primed && _cursors.TrueForAll(c => c.Peek is null));

    public long Scanned => Interlocked.Read(ref _scanned);

    public IReadOnlyList<(string Channel, string Error)> Errors => _errors;

    public IReadOnlyList<EventItem> ReadNext(int count, CancellationToken cancellationToken)
    {
        // Priming reads every channel up to its first match, which with a rare text can mean the whole log;
        // it happens here rather than in the constructor so it counts as scanning and can be cancelled.
        if (!_primed)
        {
            for (var i = 0; i < _cursors.Count && !cancellationToken.IsCancellationRequested; i++)
                _cursors[i] = (_cursors[i].Cursor, Advance(_cursors[i].Cursor, cancellationToken));
            _primed = !cancellationToken.IsCancellationRequested;
        }

        var result = new List<EventItem>(Math.Min(count, 512));
        while (result.Count < count && !cancellationToken.IsCancellationRequested)
        {
            var best = -1;
            for (var i = 0; i < _cursors.Count; i++)
            {
                var peek = _cursors[i].Peek;
                if (peek is null) continue;
                if (best < 0 || peek.TimeCreated > _cursors[best].Peek!.TimeCreated)
                    best = i;
            }
            if (best < 0) break;

            var (cursor, item) = _cursors[best];
            result.Add(item!);
            _cursors[best] = (cursor, Advance(cursor, cancellationToken));
        }
        if (cancellationToken.IsCancellationRequested)
            _cancelled = true;
        return result;
    }

    /// <summary>Reads the cursor until an event passes the client-side filter, or it runs dry.</summary>
    private EventItem? Advance(ICursor cursor, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            EventItem? item;
            try
            {
                item = cursor.TryReadNext();
            }
            catch (Exception ex)
            {
                _errors.Add((cursor.Channel, ex.Message));
                return null;
            }
            if (item is null) return null;
            Interlocked.Increment(ref _scanned);
            if (_filter.MatchesClientSide(item)) return item;
        }

        // A cancelled read leaves the cursor in an unknown position; the query is over.
        _cancelled = true;
        return null;
    }

    public void Dispose()
    {
        foreach (var (cursor, _) in _cursors)
            cursor.Dispose();
        _cursors.Clear();
    }
}
