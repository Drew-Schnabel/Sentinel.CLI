using System.Globalization;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Spans;

namespace Sentinel.CLI.Domain.Tests.TestHelpers;

// Concise span construction over Span.Create with fixed, deterministic timestamps.
// Never uses DateTimeOffset.UtcNow — every fixture derives from Epoch.
internal static class SpanBuilder
{
    public static readonly DateTimeOffset Epoch = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    public const string DefaultTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

    public static Span Make(
        string spanId,
        string? parentSpanId = null,
        string traceId = DefaultTraceId,
        string service = "svc-a",
        string name = "op",
        int startMs = 0,
        int durationMs = 10,
        SpanStatus? status = null) =>
        Span.Create(
            TraceId.Parse(traceId),
            SpanId.Parse(spanId),
            parentSpanId is null ? null : SpanId.Parse(parentSpanId),
            ServiceName.From(service),
            name,
            SpanKind.Internal,
            status ?? SpanStatus.Ok,
            Epoch.AddMilliseconds(startMs),
            Epoch.AddMilliseconds(startMs + durationMs));

    // Readable 16-hex span id from a small integer (1 → "0000000000000001").
    public static string Sid(int n) => n.ToString("x16", CultureInfo.InvariantCulture);

    public static Trace TraceOf(params Span[] spans)
    {
        var trace = Trace.Empty(
            spans.Length > 0 ? spans[0].TraceId : TraceId.Parse(DefaultTraceId));
        foreach (var span in spans)
        {
            trace.Record(span);
        }
        return trace;
    }
}
