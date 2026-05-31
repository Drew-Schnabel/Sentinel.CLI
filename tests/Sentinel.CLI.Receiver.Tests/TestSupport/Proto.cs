using Google.Protobuf;
using OtlpCommon = OpenTelemetry.Proto.Common.V1;
using OtlpLogs = OpenTelemetry.Proto.Logs.V1;
using OtlpMetrics = OpenTelemetry.Proto.Metrics.V1;
using OtlpResource = OpenTelemetry.Proto.Resource.V1;
using OtlpTrace = OpenTelemetry.Proto.Trace.V1;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Sentinel.CLI.Receiver.Tests.TestSupport;

// Builds OTLP wire messages for tests. Ids are raw bytes (16 for trace, 8 for span)
// exactly as a real exporter sends them.
internal static class Proto
{
    public static byte[] TraceIdBytes(byte seed = 1)
    {
        var bytes = new byte[16];
        bytes[15] = seed;
        return bytes;
    }

    public static byte[] SpanIdBytes(byte seed = 1)
    {
        var bytes = new byte[8];
        bytes[7] = seed;
        return bytes;
    }

    public static OtlpTrace.Span Span(
        byte[]? traceId = null,
        byte[]? spanId = null,
        byte[]? parentSpanId = null,
        string name = "op",
        long startNanos = 1_000_000_000,
        long endNanos = 1_010_000_000,
        OtlpTrace.Span.Types.SpanKind kind = OtlpTrace.Span.Types.SpanKind.Server,
        OtlpTrace.Status.Types.StatusCode statusCode = OtlpTrace.Status.Types.StatusCode.Ok)
    {
        var span = new OtlpTrace.Span
        {
            TraceId = ByteString.CopyFrom(traceId ?? TraceIdBytes()),
            SpanId = ByteString.CopyFrom(spanId ?? SpanIdBytes()),
            Name = name,
            Kind = kind,
            StartTimeUnixNano = (ulong)startNanos,
            EndTimeUnixNano = (ulong)endNanos,
            Status = new OtlpTrace.Status { Code = statusCode },
        };
        if (parentSpanId is not null)
        {
            span.ParentSpanId = ByteString.CopyFrom(parentSpanId);
        }
        return span;
    }

    public static ExportTraceServiceRequest TraceRequest(string serviceName, params OtlpTrace.Span[] spans)
    {
        var resourceSpans = new OtlpTrace.ResourceSpans
        {
            Resource = ServiceResource(serviceName),
            ScopeSpans = { new OtlpTrace.ScopeSpans() },
        };
        resourceSpans.ScopeSpans[0].Spans.AddRange(spans);
        return new ExportTraceServiceRequest { ResourceSpans = { resourceSpans } };
    }

    public static ExportTraceServiceRequest TraceRequest(params OtlpTrace.Span[] spans)
        => TraceRequest("svc-a", spans);

    // One request carrying spans from several services (each its own ResourceSpans), e.g. a
    // cross-service trace whose spans share a trace_id but originate in different services.
    public static ExportTraceServiceRequest MultiServiceTraceRequest(
        params (string Service, OtlpTrace.Span[] Spans)[] groups)
    {
        var request = new ExportTraceServiceRequest();
        foreach (var (service, spans) in groups)
        {
            var resourceSpans = new OtlpTrace.ResourceSpans
            {
                Resource = ServiceResource(service),
                ScopeSpans = { new OtlpTrace.ScopeSpans() },
            };
            resourceSpans.ScopeSpans[0].Spans.AddRange(spans);
            request.ResourceSpans.Add(resourceSpans);
        }
        return request;
    }

    public static ExportLogsServiceRequest LogRequest(OtlpLogs.LogRecord record, string serviceName = "svc-a")
    {
        var resourceLogs = new OtlpLogs.ResourceLogs
        {
            Resource = ServiceResource(serviceName),
            ScopeLogs = { new OtlpLogs.ScopeLogs() },
        };
        resourceLogs.ScopeLogs[0].LogRecords.Add(record);
        return new ExportLogsServiceRequest { ResourceLogs = { resourceLogs } };
    }

    public static OtlpResource.Resource? ServiceResource(string? serviceName)
    {
        if (serviceName is null)
        {
            return null;
        }
        return new OtlpResource.Resource
        {
            Attributes =
            {
                new OtlpCommon.KeyValue
                {
                    Key = "service.name",
                    Value = new OtlpCommon.AnyValue { StringValue = serviceName },
                },
            },
        };
    }

    public static OtlpCommon.KeyValue Attr(string key, OtlpCommon.AnyValue value)
        => new() { Key = key, Value = value };

    public static ExportMetricsServiceRequest MetricRequest(OtlpMetrics.Metric metric, string serviceName = "svc-a")
    {
        var resourceMetrics = new OtlpMetrics.ResourceMetrics
        {
            Resource = ServiceResource(serviceName),
            ScopeMetrics = { new OtlpMetrics.ScopeMetrics() },
        };
        resourceMetrics.ScopeMetrics[0].Metrics.Add(metric);
        return new ExportMetricsServiceRequest { ResourceMetrics = { resourceMetrics } };
    }

    public static OtlpMetrics.Metric Gauge(string name, double value, long timeNanos = 1_000_000_000)
    {
        var metric = new OtlpMetrics.Metric { Name = name, Unit = "1", Gauge = new OtlpMetrics.Gauge() };
        metric.Gauge.DataPoints.Add(new OtlpMetrics.NumberDataPoint { TimeUnixNano = (ulong)timeNanos, AsDouble = value });
        return metric;
    }

    public static OtlpMetrics.Metric Sum(string name, long value, long timeNanos = 1_000_000_000)
    {
        var metric = new OtlpMetrics.Metric { Name = name, Unit = "1", Sum = new OtlpMetrics.Sum() };
        metric.Sum.DataPoints.Add(new OtlpMetrics.NumberDataPoint { TimeUnixNano = (ulong)timeNanos, AsInt = value });
        return metric;
    }

    public static OtlpMetrics.Metric Histogram(
        string name, ulong count, double sum, double min, double max, long timeNanos = 1_000_000_000)
    {
        var metric = new OtlpMetrics.Metric { Name = name, Unit = "ms", Histogram = new OtlpMetrics.Histogram() };
        metric.Histogram.DataPoints.Add(new OtlpMetrics.HistogramDataPoint
        {
            TimeUnixNano = (ulong)timeNanos,
            Count = count,
            Sum = sum,
            Min = min,
            Max = max,
        });
        return metric;
    }

    public static OtlpMetrics.Metric Summary(string name)
        => new() { Name = name, Summary = new OtlpMetrics.Summary() }; // unsupported kind
}
