using System.Text.Json;
using System.Text.Json.Serialization;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Logs;
using Sentinel.CLI.Domain.Telemetry.Spans;

namespace Sentinel.CLI.Application.Serialization;

// Maps domain objects to the export DTOs and serializes them to JSON. Pure and synchronous — the
// caller supplies the already-loaded spans/logs. The AttributeValue union is flattened to its
// underlying primitive (string/long/double/bool/string[]) so the JSON is natural to read and tools
// can consume it without knowing the union.
public static class TraceExporter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ExportedTrace Build(
        TraceId traceId, IReadOnlyList<Span> spans, IReadOnlyList<LogRecord> logs)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(logs);

        return new ExportedTrace(
            traceId.Value,
            spans.Select(MapSpan).ToList(),
            logs.Select(MapLog).ToList());
    }

    public static string ToJson(
        TraceId traceId, IReadOnlyList<Span> spans, IReadOnlyList<LogRecord> logs)
        => JsonSerializer.Serialize(Build(traceId, spans, logs), Options);

    private static ExportedSpan MapSpan(Span span) => new(
        span.SpanId.Value,
        span.ParentSpanId?.Value,
        span.Service.Value,
        span.Name,
        span.Kind.ToString(),
        span.Status.Code.ToString(),
        span.Status.Description,
        span.StartTime,
        span.EndTime,
        MapAttributes(span.Attributes),
        span.Events.Select(e => new ExportedEvent(e.Timestamp, e.Name, MapAttributes(e.Attributes))).ToList(),
        span.Links.Select(l => new ExportedLink(l.TraceId.Value, l.SpanId.Value, MapAttributes(l.Attributes))).ToList());

    private static ExportedLog MapLog(LogRecord log) => new(
        log.Timestamp,
        log.Severity.ToString(),
        log.SeverityText,
        log.Service.Value,
        log.Body,
        log.SpanId?.Value,
        MapAttributes(log.Attributes));

    private static Dictionary<string, object?> MapAttributes(TelemetryAttributes attributes)
    {
        var map = new Dictionary<string, object?>(attributes.Count, StringComparer.Ordinal);
        foreach (var (key, value) in attributes)
        {
            map[key] = MapValue(value);
        }
        return map;
    }

    // Flatten the AttributeValue union to a JSON-native value.
    private static object? MapValue(AttributeValue value) => value switch
    {
        AttributeValue.Text t => t.Value,
        AttributeValue.Integer i => i.Value,
        AttributeValue.Number n => n.Value,
        AttributeValue.Flag f => f.Value,
        AttributeValue.TextList l => l.Values,
        _ => null,
    };
}
