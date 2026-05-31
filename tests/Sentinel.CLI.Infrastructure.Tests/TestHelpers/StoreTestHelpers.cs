using System.Globalization;
using Microsoft.Extensions.Options;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Logs;
using Sentinel.CLI.Domain.Telemetry.Metrics;
using Sentinel.CLI.Domain.Telemetry.Spans;
using Sentinel.CLI.Infrastructure.Telemetry;

namespace Sentinel.CLI.Infrastructure.Tests.TestHelpers;

internal static class StoreTestHelpers
{
    public static readonly DateTimeOffset Epoch = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static InMemoryTelemetryStore NewStore(
        int maxTraces = 500, int maxLogStream = 5_000, IngestGate? gate = null) =>
        new(Options.Create(new StoreOptions { MaxTraces = maxTraces, MaxLogStream = maxLogStream }),
            gate ?? new IngestGate());

    public static InMemoryMetricStore NewMetricStore(int maxSeries = 2_000, IngestGate? gate = null) =>
        new(Options.Create(new StoreOptions { MaxMetricSeries = maxSeries }), gate ?? new IngestGate());

    public static MetricPoint MakeMetric(string name, double value, string service = "svc", params (string Key, string Value)[] labels)
    {
        TelemetryAttributes? attrs = null;
        if (labels.Length > 0)
        {
            var dict = new Dictionary<string, AttributeValue>(StringComparer.Ordinal);
            foreach (var (key, val) in labels)
            {
                dict[key] = new AttributeValue.Text(val);
            }
            attrs = TelemetryAttributes.From(dict);
        }
        return MetricPoint.Number(ServiceName.From(service), name, "1", MetricKind.Gauge, Epoch, value, attrs);
    }

    // Non-zero 32-hex trace id; TraceId.Parse rejects all-zeros, so n must be >= 1.
    public static TraceId TraceId(int n) =>
        Domain.Telemetry.Common.TraceId.Parse(n.ToString("x", CultureInfo.InvariantCulture).PadLeft(32, '0'));

    // Non-zero 16-hex span id; SpanId.Parse rejects all-zeros, so n must be >= 1.
    public static SpanId SpanId(int n) =>
        Domain.Telemetry.Common.SpanId.Parse(n.ToString("x", CultureInfo.InvariantCulture).PadLeft(16, '0'));

    public static Span MakeSpan(
        TraceId traceId,
        SpanId spanId,
        SpanId? parentSpanId = null,
        int startMs = 0,
        int durationMs = 10) =>
        Span.Create(
            traceId,
            spanId,
            parentSpanId,
            ServiceName.From("svc-a"),
            "op",
            SpanKind.Internal,
            SpanStatus.Ok,
            Epoch.AddMilliseconds(startMs),
            Epoch.AddMilliseconds(startMs + durationMs));

    public static LogRecord MakeLog(
        TraceId? traceId = null,
        SpanId? spanId = null,
        string body = "log",
        int atMs = 0) =>
        LogRecord.Create(
            Epoch.AddMilliseconds(atMs),
            LogSeverity.Info,
            ServiceName.From("svc-a"),
            body,
            traceId,
            spanId);

    public static async Task<List<T>> Collect<T>(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (var item in source)
        {
            result.Add(item);
        }
        return result;
    }
}
