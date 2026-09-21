using System.Diagnostics.CodeAnalysis;

namespace EventBlitz.Core.Models;

/// <summary>
/// Event ID filter typed by the user: "1000, 1001-1010, !4624" — comma or space separated, ranges with a dash,
/// a leading "!" or "-" excludes. Includes are or-ed, excludes always win.
/// </summary>
public sealed class EventIdSpec
{
    public IReadOnlyList<(int From, int To)> Includes { get; }
    public IReadOnlyList<(int From, int To)> Excludes { get; }

    public bool IsEmpty => Includes.Count == 0 && Excludes.Count == 0;

    private EventIdSpec(List<(int, int)> includes, List<(int, int)> excludes)
    {
        Includes = includes;
        Excludes = excludes;
    }

    public static readonly EventIdSpec Empty = new([], []);

    /// <summary>Parses the text; returns false with the offending token when it is not an ID list.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out EventIdSpec? spec, out string? error)
    {
        spec = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            spec = Empty;
            return true;
        }

        var includes = new List<(int, int)>();
        var excludes = new List<(int, int)>();
        foreach (var raw in text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var token = raw;
            var exclude = false;
            if (token.StartsWith('!') || token.StartsWith('-'))
            {
                exclude = true;
                token = token[1..];
            }

            var dash = token.IndexOf('-');
            int from, to;
            if (dash > 0)
            {
                if (!int.TryParse(token[..dash], out from) || !int.TryParse(token[(dash + 1)..], out to))
                {
                    error = raw;
                    return false;
                }
                if (to < from) (from, to) = (to, from);
            }
            else if (int.TryParse(token, out from))
            {
                to = from;
            }
            else
            {
                error = raw;
                return false;
            }

            (exclude ? excludes : includes).Add((from, to));
        }

        spec = new EventIdSpec(includes, excludes);
        return true;
    }

    public bool Matches(int eventId)
    {
        foreach (var (from, to) in Excludes)
            if (eventId >= from && eventId <= to) return false;
        if (Includes.Count == 0) return true;
        foreach (var (from, to) in Includes)
            if (eventId >= from && eventId <= to) return true;
        return false;
    }

    /// <summary>XPath predicate fragment on the System element, or null when nothing is constrained.</summary>
    public string? ToXPath()
    {
        var parts = new List<string>();
        if (Includes.Count > 0)
            parts.Add("(" + string.Join(" or ", Includes.Select(Range)) + ")");
        foreach (var (from, to) in Excludes)
            parts.Add("not(" + Range((from, to)) + ")");
        return parts.Count == 0 ? null : string.Join(" and ", parts);

        static string Range((int From, int To) r) => r.From == r.To
            ? $"EventID={r.From}"
            : $"(EventID>={r.From} and EventID<={r.To})";
    }
}
