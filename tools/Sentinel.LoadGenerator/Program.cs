using System.Net.Http.Headers;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OtlpCommon = OpenTelemetry.Proto.Common.V1;
using OtlpLogs = OpenTelemetry.Proto.Logs.V1;
using OtlpMetrics = OpenTelemetry.Proto.Metrics.V1;
using OtlpResource = OpenTelemetry.Proto.Resource.V1;
using OtlpTrace = OpenTelemetry.Proto.Trace.V1;

// Emits a synthetic cross-service checkout trace (+ correlated logs) to a running Sentinel
// over OTLP/HTTP, on a loop, so the live receiver → store → TUI path can be watched on a real
// terminal. Run `sentinel` in one terminal and this in another.
//
//   dotnet run --project tools/Sentinel.LoadGenerator [-- --endpoint=http://localhost:4318]

var endpoint = ArgValue("--endpoint=") ?? "http://localhost:4318";
var maxCount = int.TryParse(ArgValue("--count="), out var parsed) && parsed > 0 ? parsed : 0; // 0 = run until Ctrl-C

using var http = new HttpClient { BaseAddress = new Uri(endpoint) };
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var limit = maxCount == 0 ? "until Ctrl-C" : $"{maxCount} times";
Console.WriteLine($"Emitting OTLP/HTTP traces (alternating checkout / inventory-sync) to {endpoint} every 1.5s, {limit}.");

var seq = 0;
while (!cts.IsCancellationRequested && (maxCount == 0 || seq < maxCount))
{
    seq++;
    var (traces, logs) = BuildBatch(seq);
    try
    {
        await Post("/v1/traces", traces);
        await Post("/v1/logs", logs);
        await Post("/v1/metrics", BuildMetrics(seq));
        var checkout = seq % 2 == 1;
        var note = seq % 5 == 0 ? " (with simulated error)" : string.Empty;
        if (checkout && seq % 7 == 0) { note += " (chatty: +50 logs)"; }
        var shape = checkout ? "checkout · 3 services, 4 spans" : "inventory-sync · 2 services, 2 spans";
        Console.WriteLine($"[{DateTimeOffset.Now:HH:mm:ss}] sent trace #{seq} — {shape} + metrics{note}");
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cts.IsCancellationRequested)
    {
        Console.WriteLine($"  send failed: {ex.Message} — is Sentinel running on {endpoint}?");
    }

    if (maxCount == 0 || seq < maxCount)
    {
        try { await Task.Delay(TimeSpan.FromMilliseconds(1500), cts.Token); }
        catch (TaskCanceledException) { /* shutting down */ }
    }
}

Console.WriteLine("stopped.");
return 0;

string? ArgValue(string prefix)
    => args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

async Task Post(string path, IMessage message)
{
    using var content = new ByteArrayContent(message.ToByteArray());
    content.Headers.ContentType = new MediaTypeHeaderValue("application/x-protobuf");
    using var response = await http.PostAsync(path, content, cts.Token);
    response.EnsureSuccessStatusCode();
}

// Alternate two trace shapes rooted in different services, so the trace list (colored by root
// service) shows more than one color and the two can be compared side by side.
static (ExportTraceServiceRequest Traces, ExportLogsServiceRequest Logs) BuildBatch(int seq)
    => seq % 2 == 1 ? BuildCheckout(seq) : BuildInventorySync(seq);

static (ExportTraceServiceRequest Traces, ExportLogsServiceRequest Logs) BuildCheckout(int seq)
{
    var traceId = Bytes(16, seq);
    var rootId = Bytes(8, seq * 10 + 1);
    var chargeId = Bytes(8, seq * 10 + 2);
    var dbId = Bytes(8, seq * 10 + 3);
    var emailId = Bytes(8, seq * 10 + 4);
    var failed = seq % 5 == 0;

    var start = (ulong)((DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).Ticks * 100L);
    ulong At(double ms) => start + (ulong)(ms * 1_000_000.0);

    var traces = new ExportTraceServiceRequest
    {
        ResourceSpans =
        {
            SpanGroup("orders-api",
                Span(traceId, rootId, null, "POST /api/checkout", OtlpTrace.Span.Types.SpanKind.Server, start, At(480), failed)),
            SpanGroup("payment-service",
                Span(traceId, chargeId, rootId, "Charge.Authorize", OtlpTrace.Span.Types.SpanKind.Internal, At(40), At(300), failed),
                Span(traceId, dbId, chargeId, "INSERT payments", OtlpTrace.Span.Types.SpanKind.Client, At(120), At(200), error: false)),
            SpanGroup("notification-service",
                Span(traceId, emailId, rootId, "email.send", OtlpTrace.Span.Types.SpanKind.Producer, At(320), At(470), error: false)),
        },
    };

    var ordersLogs = new List<OtlpLogs.LogRecord>
    {
        Log(traceId, rootId, OtlpLogs.SeverityNumber.Info, $"checkout received seq={seq}", start),
    };
    // Every 7th trace is "chatty": a burst of ~50 correlated logs cycling through every
    // severity, so the per-trace Logs pane scrolls — the case that exercises row coloring
    // across a list taller than the pane.
    if (seq % 7 == 0)
    {
        OtlpLogs.SeverityNumber[] levels =
        [
            OtlpLogs.SeverityNumber.Trace, OtlpLogs.SeverityNumber.Debug, OtlpLogs.SeverityNumber.Info,
            OtlpLogs.SeverityNumber.Warn, OtlpLogs.SeverityNumber.Error,
        ];
        for (var i = 0; i < 50; i++)
        {
            var level = levels[i % levels.Length];
            ordersLogs.Add(Log(traceId, rootId, level, $"checkout step {i + 1:D2} [{level}]", At(i * 9)));
        }
    }

    var logs = new ExportLogsServiceRequest
    {
        ResourceLogs =
        {
            LogGroup("orders-api", [.. ordersLogs]),
            LogGroup("payment-service",
                Log(traceId, chargeId,
                    failed ? OtlpLogs.SeverityNumber.Error : OtlpLogs.SeverityNumber.Debug,
                    failed ? "charge declined: insufficient funds" : "charge authorized", At(45))),
            LogGroup("notification-service",
                Log(traceId, emailId, OtlpLogs.SeverityNumber.Warn,
                    "email provider latency high; delivery may be delayed", At(330))),
        },
    };

    return (traces, logs);
}

static (ExportTraceServiceRequest Traces, ExportLogsServiceRequest Logs) BuildInventorySync(int seq)
{
    var traceId = Bytes(16, seq);
    var rootId = Bytes(8, seq * 10 + 1);
    var queryId = Bytes(8, seq * 10 + 2);
    var failed = seq % 5 == 0;

    var start = (ulong)((DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).Ticks * 100L);
    ulong At(double ms) => start + (ulong)(ms * 1_000_000.0);

    var traces = new ExportTraceServiceRequest
    {
        ResourceSpans =
        {
            SpanGroup("inventory-service",
                Span(traceId, rootId, null, "GET /api/inventory/sync", OtlpTrace.Span.Types.SpanKind.Server, start, At(260), failed)),
            SpanGroup("warehouse-db",
                Span(traceId, queryId, rootId, "SELECT stock_levels", OtlpTrace.Span.Types.SpanKind.Client, At(30), At(230), error: false)),
        },
    };

    var logs = new ExportLogsServiceRequest
    {
        ResourceLogs =
        {
            LogGroup("inventory-service",
                Log(traceId, rootId,
                    failed ? OtlpLogs.SeverityNumber.Error : OtlpLogs.SeverityNumber.Info,
                    failed ? "inventory sync failed: warehouse timeout" : "inventory sync complete", start)),
            LogGroup("warehouse-db",
                Log(traceId, queryId, OtlpLogs.SeverityNumber.Debug, "queried 4213 SKUs", At(40))),
        },
    };

    return (traces, logs);
}

static ExportMetricsServiceRequest BuildMetrics(int seq)
{
    var now = (ulong)((DateTimeOffset.UtcNow - DateTimeOffset.UnixEpoch).Ticks * 100L);

    var gauge = new OtlpMetrics.Metric { Name = "process.cpu.usage", Unit = "1", Gauge = new OtlpMetrics.Gauge() };
    gauge.Gauge.DataPoints.Add(new OtlpMetrics.NumberDataPoint { TimeUnixNano = now, AsDouble = 0.2 + (seq % 10 * 0.05) });

    var sum = new OtlpMetrics.Metric { Name = "http.server.requests", Unit = "1", Sum = new OtlpMetrics.Sum { IsMonotonic = true } };
    sum.Sum.DataPoints.Add(new OtlpMetrics.NumberDataPoint { TimeUnixNano = now, AsInt = seq });

    var histogram = new OtlpMetrics.Metric { Name = "http.server.duration", Unit = "ms", Histogram = new OtlpMetrics.Histogram() };
    histogram.Histogram.DataPoints.Add(new OtlpMetrics.HistogramDataPoint
    {
        TimeUnixNano = now,
        Count = (ulong)seq,
        Sum = seq * 12.5,
        Min = 1,
        Max = 480,
    });

    var resourceMetrics = new OtlpMetrics.ResourceMetrics
    {
        Resource = Resource("payment-service"),
        ScopeMetrics = { new OtlpMetrics.ScopeMetrics() },
    };
    resourceMetrics.ScopeMetrics[0].Metrics.Add(gauge);
    resourceMetrics.ScopeMetrics[0].Metrics.Add(sum);
    resourceMetrics.ScopeMetrics[0].Metrics.Add(histogram);
    return new ExportMetricsServiceRequest { ResourceMetrics = { resourceMetrics } };
}

static OtlpTrace.Span Span(
    byte[] traceId, byte[] spanId, byte[]? parentSpanId, string name,
    OtlpTrace.Span.Types.SpanKind kind, ulong startNanos, ulong endNanos, bool error)
{
    var span = new OtlpTrace.Span
    {
        TraceId = ByteString.CopyFrom(traceId),
        SpanId = ByteString.CopyFrom(spanId),
        Name = name,
        Kind = kind,
        StartTimeUnixNano = startNanos,
        EndTimeUnixNano = endNanos,
        Status = new OtlpTrace.Status
        {
            Code = error ? OtlpTrace.Status.Types.StatusCode.Error : OtlpTrace.Status.Types.StatusCode.Ok,
            Message = error ? "simulated failure" : string.Empty,
        },
    };
    if (parentSpanId is not null)
    {
        span.ParentSpanId = ByteString.CopyFrom(parentSpanId);
    }
    return span;
}

static OtlpTrace.ResourceSpans SpanGroup(string service, params OtlpTrace.Span[] spans)
{
    var group = new OtlpTrace.ResourceSpans { Resource = Resource(service), ScopeSpans = { new OtlpTrace.ScopeSpans() } };
    group.ScopeSpans[0].Spans.AddRange(spans);
    return group;
}

static OtlpLogs.LogRecord Log(byte[] traceId, byte[] spanId, OtlpLogs.SeverityNumber severity, string body, ulong timeNanos)
    => new()
    {
        TimeUnixNano = timeNanos,
        SeverityNumber = severity,
        Body = new OtlpCommon.AnyValue { StringValue = body },
        TraceId = ByteString.CopyFrom(traceId),
        SpanId = ByteString.CopyFrom(spanId),
    };

static OtlpLogs.ResourceLogs LogGroup(string service, params OtlpLogs.LogRecord[] records)
{
    var group = new OtlpLogs.ResourceLogs { Resource = Resource(service), ScopeLogs = { new OtlpLogs.ScopeLogs() } };
    group.ScopeLogs[0].LogRecords.AddRange(records);
    return group;
}

static OtlpResource.Resource Resource(string service) => new()
{
    Attributes =
    {
        new OtlpCommon.KeyValue { Key = "service.name", Value = new OtlpCommon.AnyValue { StringValue = service } },
    },
};

// Non-zero id bytes encoding the sequence; 16 bytes for a trace id, 8 for a span id.
static byte[] Bytes(int length, int value)
{
    var bytes = new byte[length];
    var v = (uint)value;
    bytes[length - 1] = (byte)v;
    bytes[length - 2] = (byte)(v >> 8);
    bytes[length - 3] = (byte)(v >> 16);
    bytes[0] = 0x01; // keep it non-zero regardless of value
    return bytes;
}
