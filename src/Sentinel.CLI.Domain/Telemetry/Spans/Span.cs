using Sentinel.CLI.Domain.Telemetry.Common;

namespace Sentinel.CLI.Domain.Telemetry.Spans;

public sealed class Span
{
    public TraceId TraceId { get; }
    public SpanId SpanId { get; }
    public SpanId? ParentSpanId { get; }
    public ServiceName Service { get; }
    public string Name { get; }
    public SpanKind Kind { get; }
    public SpanStatus Status { get; }
    public DateTimeOffset StartTime { get; }
    public DateTimeOffset EndTime { get; }
    public TelemetryAttributes Attributes { get; }
    public IReadOnlyList<SpanEvent> Events { get; }
    public IReadOnlyList<SpanLink> Links { get; }

    public TimeSpan Duration => EndTime - StartTime;

    private Span(
        TraceId traceId,
        SpanId spanId,
        SpanId? parentSpanId,
        ServiceName service,
        string name,
        SpanKind kind,
        SpanStatus status,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        TelemetryAttributes attributes,
        IReadOnlyList<SpanEvent> events,
        IReadOnlyList<SpanLink> links)
    {
        TraceId = traceId;
        SpanId = spanId;
        ParentSpanId = parentSpanId;
        Service = service;
        Name = name;
        Kind = kind;
        Status = status;
        StartTime = startTime;
        EndTime = endTime;
        Attributes = attributes;
        Events = events;
        Links = links;
    }

    public static Span Create(
        TraceId traceId,
        SpanId spanId,
        SpanId? parentSpanId,
        ServiceName service,
        string name,
        SpanKind kind,
        SpanStatus status,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        TelemetryAttributes? attributes = null,
        IReadOnlyList<SpanEvent>? events = null,
        IReadOnlyList<SpanLink>? links = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(status);
        if (endTime < startTime)
        {
            throw new ArgumentException("Span end_time precedes start_time.", nameof(endTime));
        }
        return new Span(
            traceId,
            spanId,
            parentSpanId,
            service,
            name,
            kind,
            status,
            startTime,
            endTime,
            attributes ?? TelemetryAttributes.Empty,
            events ?? [],
            links ?? []);
    }
}
