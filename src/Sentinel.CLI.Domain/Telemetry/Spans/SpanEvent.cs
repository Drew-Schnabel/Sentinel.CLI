using Sentinel.CLI.Domain.Telemetry.Common;

namespace Sentinel.CLI.Domain.Telemetry.Spans;

// A timestamped event recorded during a span (OTLP Span.Event).
public sealed record SpanEvent
{
    public DateTimeOffset Timestamp { get; }
    public string Name { get; }
    public TelemetryAttributes Attributes { get; }

    private SpanEvent(DateTimeOffset timestamp, string name, TelemetryAttributes attributes)
    {
        Timestamp = timestamp;
        Name = name;
        Attributes = attributes;
    }

    public static SpanEvent Create(
        DateTimeOffset timestamp,
        string name,
        TelemetryAttributes? attributes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new SpanEvent(timestamp, name, attributes ?? TelemetryAttributes.Empty);
    }
}
