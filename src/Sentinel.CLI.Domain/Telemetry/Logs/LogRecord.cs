using Sentinel.CLI.Domain.Telemetry.Common;

namespace Sentinel.CLI.Domain.Telemetry.Logs;

public sealed class LogRecord
{
    public DateTimeOffset Timestamp { get; }
    public LogSeverity Severity { get; }
    public ServiceName Service { get; }
    public string Body { get; }
    public TraceId? TraceId { get; }
    public SpanId? SpanId { get; }
    public TelemetryAttributes Attributes { get; }

    // Original OTLP severity_text (e.g. "WARNING"), when the exporter supplied one.
    public string? SeverityText { get; }

    private LogRecord(
        DateTimeOffset timestamp,
        LogSeverity severity,
        ServiceName service,
        string body,
        TraceId? traceId,
        SpanId? spanId,
        TelemetryAttributes attributes,
        string? severityText)
    {
        Timestamp = timestamp;
        Severity = severity;
        Service = service;
        Body = body;
        TraceId = traceId;
        SpanId = spanId;
        Attributes = attributes;
        SeverityText = severityText;
    }

    public static LogRecord Create(
        DateTimeOffset timestamp,
        LogSeverity severity,
        ServiceName service,
        string body,
        TraceId? traceId = null,
        SpanId? spanId = null,
        TelemetryAttributes? attributes = null,
        string? severityText = null)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new LogRecord(
            timestamp,
            severity,
            service,
            body,
            traceId,
            spanId,
            attributes ?? TelemetryAttributes.Empty,
            string.IsNullOrEmpty(severityText) ? null : severityText);
    }
}
