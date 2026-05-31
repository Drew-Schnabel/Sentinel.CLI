using System.Globalization;
using Google.Protobuf;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Logs;
using Sentinel.CLI.Domain.Telemetry.Metrics;
using Sentinel.CLI.Domain.Telemetry.Spans;
using OtlpCommon = OpenTelemetry.Proto.Common.V1;
using OtlpLogs = OpenTelemetry.Proto.Logs.V1;
using OtlpMetrics = OpenTelemetry.Proto.Metrics.V1;
using OtlpResource = OpenTelemetry.Proto.Resource.V1;
using OtlpTrace = OpenTelemetry.Proto.Trace.V1;

namespace Sentinel.CLI.Receiver.Telemetry;

// Anti-corruption layer: OTLP wire types → domain types. Pure and total — never
// throws on bad input. A span/log that violates a domain invariant is rejected and
// counted, not propagated, so one malformed item never fails a whole export batch.
// Used by both the gRPC services and the OTLP/HTTP endpoints, so mapping lives in
// exactly one place.
internal static class OtlpMapper
{
    public static (IReadOnlyList<Span> Items, int Rejected) MapTraces(
        IEnumerable<OtlpTrace.ResourceSpans> resourceSpans)
    {
        var spans = new List<Span>();
        var rejected = 0;
        foreach (var rs in resourceSpans)
        {
            var service = ResolveService(rs.Resource);
            var resourceTags = ResourceTags(rs.Resource);
            foreach (var scope in rs.ScopeSpans)
            {
                foreach (var protoSpan in scope.Spans)
                {
                    if (TryMapSpan(protoSpan, service, resourceTags, out var span))
                    {
                        spans.Add(span);
                    }
                    else
                    {
                        rejected++;
                    }
                }
            }
        }
        return (spans, rejected);
    }

    public static (IReadOnlyList<LogRecord> Items, int Rejected) MapLogs(
        IEnumerable<OtlpLogs.ResourceLogs> resourceLogs)
    {
        var logs = new List<LogRecord>();
        var rejected = 0;
        foreach (var rl in resourceLogs)
        {
            var service = ResolveService(rl.Resource);
            var resourceTags = ResourceTags(rl.Resource);
            foreach (var scope in rl.ScopeLogs)
            {
                foreach (var protoLog in scope.LogRecords)
                {
                    if (TryMapLog(protoLog, service, resourceTags, out var log))
                    {
                        logs.Add(log);
                    }
                    else
                    {
                        rejected++;
                    }
                }
            }
        }
        return (logs, rejected);
    }

    public static (IReadOnlyList<MetricPoint> Items, int Rejected) MapMetrics(
        IEnumerable<OtlpMetrics.ResourceMetrics> resourceMetrics)
    {
        var points = new List<MetricPoint>();
        var rejected = 0;
        foreach (var rm in resourceMetrics)
        {
            var service = ResolveService(rm.Resource);
            foreach (var scope in rm.ScopeMetrics)
            {
                foreach (var metric in scope.Metrics)
                {
                    switch (metric.DataCase)
                    {
                        case OtlpMetrics.Metric.DataOneofCase.Gauge:
                            MapNumberPoints(metric, metric.Gauge.DataPoints, MetricKind.Gauge, service, points, ref rejected);
                            break;
                        case OtlpMetrics.Metric.DataOneofCase.Sum:
                            MapNumberPoints(metric, metric.Sum.DataPoints, MetricKind.Sum, service, points, ref rejected);
                            break;
                        case OtlpMetrics.Metric.DataOneofCase.Histogram:
                            MapHistogramPoints(metric, metric.Histogram.DataPoints, service, points, ref rejected);
                            break;
                        default:
                            rejected++; // ExponentialHistogram / Summary / unset — not modeled
                            break;
                    }
                }
            }
        }
        return (points, rejected);
    }

    private static void MapNumberPoints(
        OtlpMetrics.Metric metric,
        IEnumerable<OtlpMetrics.NumberDataPoint> dataPoints,
        MetricKind kind,
        ServiceName service,
        List<MetricPoint> sink,
        ref int rejected)
    {
        foreach (var dp in dataPoints)
        {
            try
            {
                var value = dp.ValueCase == OtlpMetrics.NumberDataPoint.ValueOneofCase.AsInt
                    ? dp.AsInt
                    : dp.AsDouble;
                sink.Add(MetricPoint.Number(
                    service, metric.Name, metric.Unit, kind,
                    ToTimestamp(dp.TimeUnixNano), value, MapAttributes(dp.Attributes)));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                rejected++;
            }
        }
    }

    private static void MapHistogramPoints(
        OtlpMetrics.Metric metric,
        IEnumerable<OtlpMetrics.HistogramDataPoint> dataPoints,
        ServiceName service,
        List<MetricPoint> sink,
        ref int rejected)
    {
        foreach (var dp in dataPoints)
        {
            try
            {
                sink.Add(MetricPoint.Histogram(
                    service, metric.Name, metric.Unit, ToTimestamp(dp.TimeUnixNano),
                    (long)dp.Count,
                    dp.HasSum ? dp.Sum : null,
                    dp.HasMin ? dp.Min : null,
                    dp.HasMax ? dp.Max : null,
                    MapAttributes(dp.Attributes)));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                rejected++;
            }
        }
    }

    private static bool TryMapSpan(
        OtlpTrace.Span proto,
        ServiceName service,
        IReadOnlyList<KeyValuePair<string, AttributeValue>> resourceTags,
        out Span span)
    {
        span = null!;
        try
        {
            // The span's own ids are strict — an invalid/zero id is a genuine drop.
            var traceId = TraceId.Parse(Convert.ToHexStringLower(proto.TraceId.Span));
            var spanId = SpanId.Parse(Convert.ToHexStringLower(proto.SpanId.Span));

            // parent_span_id is lenient: empty means "root", and a malformed/zero parent
            // ref should not cost us an otherwise-valid span — it becomes an orphan root.
            var parentSpanId = OptionalSpanId(proto.ParentSpanId);

            span = Span.Create(
                traceId,
                spanId,
                parentSpanId,
                service,
                proto.Name,
                MapKind(proto.Kind),
                MapStatus(proto.Status),
                ToTimestamp(proto.StartTimeUnixNano),
                ToTimestamp(proto.EndTimeUnixNano),
                MapAttributes(proto.Attributes, resourceTags),
                MapEvents(proto.Events),
                MapLinks(proto.Links));
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryMapLog(
        OtlpLogs.LogRecord proto,
        ServiceName service,
        IReadOnlyList<KeyValuePair<string, AttributeValue>> resourceTags,
        out LogRecord log)
    {
        log = null!;
        try
        {
            log = LogRecord.Create(
                ToTimestamp(proto.TimeUnixNano),
                MapSeverity(proto.SeverityNumber),
                service,
                BodyText(proto.Body),
                OptionalTraceId(proto.TraceId),
                OptionalSpanId(proto.SpanId),
                MapAttributes(proto.Attributes, resourceTags),
                proto.SeverityText);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            return false;
        }
    }

    private static ServiceName ResolveService(OtlpResource.Resource? resource)
    {
        if (resource is not null)
        {
            foreach (var attr in resource.Attributes)
            {
                if (attr.Key == "service.name"
                    && attr.Value?.ValueCase == OtlpCommon.AnyValue.ValueOneofCase.StringValue
                    && !string.IsNullOrWhiteSpace(attr.Value.StringValue))
                {
                    return ServiceName.From(attr.Value.StringValue);
                }
            }
        }
        return ServiceName.From("unknown");
    }

    // Empty (root), wrong length, or all-zero → no id. Never throws.
    private static SpanId? OptionalSpanId(ByteString bytes)
    {
        if (bytes.Length != 8)
        {
            return null;
        }
        var hex = Convert.ToHexStringLower(bytes.Span);
        return IsAllZero(hex) ? null : SpanId.Parse(hex);
    }

    private static TraceId? OptionalTraceId(ByteString bytes)
    {
        if (bytes.Length != 16)
        {
            return null;
        }
        var hex = Convert.ToHexStringLower(bytes.Span);
        return IsAllZero(hex) ? null : TraceId.Parse(hex);
    }

    private static bool IsAllZero(string hex)
    {
        foreach (var c in hex)
        {
            if (c != '0')
            {
                return false;
            }
        }
        return true;
    }

    private static DateTimeOffset ToTimestamp(ulong unixNanos)
        => DateTimeOffset.UnixEpoch.AddTicks((long)(unixNanos / 100UL)); // 1 tick = 100ns

    private static SpanKind MapKind(OtlpTrace.Span.Types.SpanKind kind) => kind switch
    {
        OtlpTrace.Span.Types.SpanKind.Internal => SpanKind.Internal,
        OtlpTrace.Span.Types.SpanKind.Server => SpanKind.Server,
        OtlpTrace.Span.Types.SpanKind.Client => SpanKind.Client,
        OtlpTrace.Span.Types.SpanKind.Producer => SpanKind.Producer,
        OtlpTrace.Span.Types.SpanKind.Consumer => SpanKind.Consumer,
        _ => SpanKind.Unspecified,
    };

    private static SpanStatus MapStatus(OtlpTrace.Status? status) => status?.Code switch
    {
        OtlpTrace.Status.Types.StatusCode.Ok => SpanStatus.Ok,
        OtlpTrace.Status.Types.StatusCode.Error =>
            SpanStatus.Error(string.IsNullOrEmpty(status.Message) ? null : status.Message),
        _ => SpanStatus.Unset,
    };

    private static LogSeverity MapSeverity(OtlpLogs.SeverityNumber severity) => (int)severity switch
    {
        >= 21 => LogSeverity.Fatal,
        >= 17 => LogSeverity.Error,
        >= 13 => LogSeverity.Warn,
        >= 9 => LogSeverity.Info,
        >= 5 => LogSeverity.Debug,
        >= 1 => LogSeverity.Trace,
        _ => LogSeverity.Unspecified,
    };

    // A malformed individual event/link is skipped rather than dropping the whole span —
    // consistent with the lenient parent-id handling above.
    private static List<SpanEvent> MapEvents(IEnumerable<OtlpTrace.Span.Types.Event> events)
    {
        var result = new List<SpanEvent>();
        foreach (var protoEvent in events)
        {
            try
            {
                result.Add(SpanEvent.Create(
                    ToTimestamp(protoEvent.TimeUnixNano),
                    protoEvent.Name,
                    MapAttributes(protoEvent.Attributes)));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                // skip this event, keep the span
            }
        }
        return result;
    }

    private static List<SpanLink> MapLinks(IEnumerable<OtlpTrace.Span.Types.Link> links)
    {
        var result = new List<SpanLink>();
        foreach (var protoLink in links)
        {
            var traceId = OptionalTraceId(protoLink.TraceId);
            var spanId = OptionalSpanId(protoLink.SpanId);
            if (traceId is null || spanId is null)
            {
                continue; // a link without a valid target is meaningless — skip it, keep the span
            }
            result.Add(SpanLink.Create(traceId.Value, spanId.Value, MapAttributes(protoLink.Attributes)));
        }
        return result;
    }

    private static TelemetryAttributes MapAttributes(IEnumerable<OtlpCommon.KeyValue> attributes)
        => MapAttributes(attributes, []);

    private static TelemetryAttributes MapAttributes(
        IEnumerable<OtlpCommon.KeyValue> attributes,
        IReadOnlyList<KeyValuePair<string, AttributeValue>> resourceTags)
    {
        var values = new Dictionary<string, AttributeValue>(StringComparer.Ordinal);
        foreach (var tag in resourceTags)
        {
            values[tag.Key] = tag.Value; // resource.* first; own attributes win on collision
        }
        foreach (var kv in attributes)
        {
            if (string.IsNullOrWhiteSpace(kv.Key))
            {
                continue;
            }
            var mapped = MapValue(kv.Value);
            if (mapped is not null)
            {
                values[kv.Key] = mapped;
            }
        }
        return TelemetryAttributes.From(values);
    }

    private static readonly string[] ResourceTagKeys =
        ["service.namespace", "service.version", "service.instance.id", "host.name", "deployment.environment"];

    // Selected resource attributes, prefixed with "resource." so they read clearly alongside
    // a span/log's own attributes. Folded into the attribute map to avoid a new domain type.
    private static List<KeyValuePair<string, AttributeValue>> ResourceTags(OtlpResource.Resource? resource)
    {
        var tags = new List<KeyValuePair<string, AttributeValue>>();
        if (resource is null)
        {
            return tags;
        }
        foreach (var attr in resource.Attributes)
        {
            if (Array.IndexOf(ResourceTagKeys, attr.Key) < 0)
            {
                continue;
            }
            var mapped = MapValue(attr.Value);
            if (mapped is not null)
            {
                tags.Add(new KeyValuePair<string, AttributeValue>($"resource.{attr.Key}", mapped));
            }
        }
        return tags;
    }

    private static AttributeValue? MapValue(OtlpCommon.AnyValue? value) => value?.ValueCase switch
    {
        OtlpCommon.AnyValue.ValueOneofCase.StringValue => new AttributeValue.Text(value.StringValue),
        OtlpCommon.AnyValue.ValueOneofCase.BoolValue => new AttributeValue.Flag(value.BoolValue),
        OtlpCommon.AnyValue.ValueOneofCase.IntValue => new AttributeValue.Integer(value.IntValue),
        OtlpCommon.AnyValue.ValueOneofCase.DoubleValue => new AttributeValue.Number(value.DoubleValue),
        OtlpCommon.AnyValue.ValueOneofCase.ArrayValue =>
            new AttributeValue.TextList([.. value.ArrayValue.Values.Select(ScalarString)]),
        // kvlist, bytes, string-table index, and unset are not represented in the domain.
        _ => null,
    };

    private static string ScalarString(OtlpCommon.AnyValue value) => value.ValueCase switch
    {
        OtlpCommon.AnyValue.ValueOneofCase.StringValue => value.StringValue,
        OtlpCommon.AnyValue.ValueOneofCase.BoolValue => value.BoolValue ? "true" : "false",
        OtlpCommon.AnyValue.ValueOneofCase.IntValue => value.IntValue.ToString(CultureInfo.InvariantCulture),
        OtlpCommon.AnyValue.ValueOneofCase.DoubleValue => value.DoubleValue.ToString(CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };

    private static string BodyText(OtlpCommon.AnyValue? body) => body?.ValueCase switch
    {
        null => string.Empty,
        OtlpCommon.AnyValue.ValueOneofCase.StringValue => body.StringValue,
        _ => ScalarString(body),
    };
}
