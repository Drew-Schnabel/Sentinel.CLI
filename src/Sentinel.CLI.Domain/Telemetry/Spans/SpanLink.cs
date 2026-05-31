using Sentinel.CLI.Domain.Telemetry.Common;

namespace Sentinel.CLI.Domain.Telemetry.Spans;

// A reference from this span to another span, possibly in a different trace (OTLP Span.Link).
public sealed record SpanLink
{
    public TraceId TraceId { get; }
    public SpanId SpanId { get; }
    public TelemetryAttributes Attributes { get; }

    private SpanLink(TraceId traceId, SpanId spanId, TelemetryAttributes attributes)
    {
        TraceId = traceId;
        SpanId = spanId;
        Attributes = attributes;
    }

    public static SpanLink Create(
        TraceId traceId,
        SpanId spanId,
        TelemetryAttributes? attributes = null)
        => new(traceId, spanId, attributes ?? TelemetryAttributes.Empty);
}
