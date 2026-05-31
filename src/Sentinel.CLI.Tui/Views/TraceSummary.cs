using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Spans;

namespace Sentinel.CLI.Tui.Views;

internal sealed record TraceSummary(
    TraceId Id,
    string RootName,
    string RootService,
    int SpanCount,
    TimeSpan Duration,
    SpanStatusCode Status,
    DateTimeOffset StartedAt)
{
    public static TraceSummary FromTrace(Trace trace)
    {
        var spans = trace.Spans.ToList();
        if (spans.Count == 0)
        {
            return new TraceSummary(
                trace.Id, "(empty)", "—", 0, TimeSpan.Zero,
                SpanStatusCode.Unset, DateTimeOffset.MinValue);
        }

        var root = trace.FindRoot();
        var rootName = root?.Name ?? "(no root)";
        var rootService = root?.Service.Value ?? spans[0].Service.Value;
        var start = spans.Min(s => s.StartTime);
        var end = spans.Max(s => s.EndTime);
        var status = spans.Any(s => s.Status.Code == SpanStatusCode.Error)
            ? SpanStatusCode.Error
            : spans.All(s => s.Status.Code == SpanStatusCode.Ok)
                ? SpanStatusCode.Ok
                : SpanStatusCode.Unset;

        return new TraceSummary(
            trace.Id, rootName, rootService, spans.Count, end - start, status, start);
    }

    public string ShortId => Id.Value[..16];
    public string DurationDisplay => Duration.TotalMilliseconds < 1000
        ? $"{Duration.TotalMilliseconds:F1} ms"
        : $"{Duration.TotalSeconds:F2} s";
    public string StatusDisplay => Status switch
    {
        SpanStatusCode.Ok => "OK",
        SpanStatusCode.Error => "ERR",
        _ => "—",
    };
}
