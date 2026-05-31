namespace Sentinel.CLI.Application.Serialization;

// Serialization DTOs for `:export` — a stable, JSON-friendly shape independent of the domain
// model (ids as strings, the AttributeValue union flattened to primitives). Spans are a flat list;
// parent_span_id preserves the tree so an importer can rebuild it via Trace.Assemble(). Kept in
// Application so a future `:import` / `--replay` can map these back to domain objects.
public sealed record ExportedTrace(
    string TraceId,
    IReadOnlyList<ExportedSpan> Spans,
    IReadOnlyList<ExportedLog> Logs);

public sealed record ExportedSpan(
    string SpanId,
    string? ParentSpanId,
    string Service,
    string Name,
    string Kind,
    string Status,
    string? StatusDescription,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyDictionary<string, object?> Attributes,
    IReadOnlyList<ExportedEvent> Events,
    IReadOnlyList<ExportedLink> Links);

public sealed record ExportedEvent(
    DateTimeOffset Timestamp,
    string Name,
    IReadOnlyDictionary<string, object?> Attributes);

public sealed record ExportedLink(
    string TraceId,
    string SpanId,
    IReadOnlyDictionary<string, object?> Attributes);

public sealed record ExportedLog(
    DateTimeOffset Timestamp,
    string Severity,
    string? SeverityText,
    string Service,
    string Body,
    string? SpanId,
    IReadOnlyDictionary<string, object?> Attributes);
