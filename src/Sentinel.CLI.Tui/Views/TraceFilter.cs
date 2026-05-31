using Sentinel.CLI.Domain.Telemetry.Spans;

namespace Sentinel.CLI.Tui.Views;

// An active trace-list filter: an optional service match, an optional status match, and zero or
// more free-text terms. All present criteria must hold (AND); a term matches if it appears in any
// span's service name OR span name (case-insensitive substring), so it searches across the whole
// cross-service trace, not just the root. Immutable; built by Create and applied in PopulateAsync.
internal sealed class TraceFilter
{
    private readonly string? _service;
    private readonly SpanStatusCode? _status;
    private readonly IReadOnlyList<string> _terms;
    private readonly TimeSpan? _since;

    private TraceFilter(
        string expression, string? service, SpanStatusCode? status, IReadOnlyList<string> terms, TimeSpan? since)
    {
        Expression = expression;
        _service = service;
        _status = status;
        _terms = terms;
        _since = since;
    }

    // Normalized human-readable form, e.g. "service=orders-api status=error checkout" — for echoing.
    public string Expression { get; }

    // Build a filter from raw command inputs. Returns (null, null) when no criteria are given
    // (the caller treats that as "clear the filter"), or (null, error) for an invalid status value.
    public static (TraceFilter? Filter, string? Error) Create(
        string? service, string? statusText, IReadOnlyList<string> terms, string? sinceText = null)
    {
        ArgumentNullException.ThrowIfNull(terms);

        SpanStatusCode? status = null;
        if (!string.IsNullOrWhiteSpace(statusText))
        {
            if (!TryParseStatus(statusText, out var code))
            {
                return (null, $"unknown status '{statusText}' — use ok, error, or unset");
            }
            status = code;
        }

        TimeSpan? since = null;
        if (!string.IsNullOrWhiteSpace(sinceText))
        {
            since = DurationParse.TryParse(sinceText);
            if (since is null)
            {
                return (null, $"invalid duration '{sinceText}' — use e.g. 30s, 5m, 2h");
            }
        }

        var cleanService = string.IsNullOrWhiteSpace(service) ? null : service.Trim();
        var cleanTerms = terms.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

        if (cleanService is null && status is null && since is null && cleanTerms.Count == 0)
        {
            return (null, null); // no criteria → clear
        }

        var parts = new List<string>();
        if (cleanService is not null)
        {
            parts.Add($"service={cleanService}");
        }
        if (status is { } st)
        {
            parts.Add($"status={st.ToString().ToLowerInvariant()}");
        }
        if (since is not null)
        {
            parts.Add($"since={sinceText!.Trim()}");
        }
        parts.AddRange(cleanTerms);

        return (new TraceFilter(string.Join(' ', parts), cleanService, status, cleanTerms, since), null);
    }

    // True if the trace passes every present criterion. `status` is the trace's aggregated status
    // (computed once by the caller, e.g. TraceSummary.FromTrace); `now` anchors the `since` window
    // (passed in once per refresh so all traces are judged against the same instant, and so tests
    // can use a fixed clock).
    public bool Matches(Trace trace, SpanStatusCode status, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(trace);

        if (_status is { } wanted && status != wanted)
        {
            return false;
        }

        var spans = trace.Spans.ToList();

        if (_since is { } window)
        {
            var start = spans.Count > 0 ? spans.Min(s => s.StartTime) : DateTimeOffset.MinValue;
            if (now - start > window)
            {
                return false;
            }
        }

        if (_service is { } svc && !spans.Any(s => Has(s.Service.Value, svc)))
        {
            return false;
        }

        foreach (var term in _terms)
        {
            if (!spans.Any(s => Has(s.Service.Value, term) || Has(s.Name, term)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Has(string haystack, string needle)
        => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static bool TryParseStatus(string text, out SpanStatusCode code)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "error":
            case "err":
                code = SpanStatusCode.Error;
                return true;
            case "ok":
                code = SpanStatusCode.Ok;
                return true;
            case "unset":
            case "none":
                code = SpanStatusCode.Unset;
                return true;
            default:
                code = default;
                return false;
        }
    }
}
