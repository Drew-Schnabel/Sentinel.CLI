using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Logs;
using Sentinel.CLI.Domain.Telemetry.Spans;

namespace Sentinel.CLI.Tui.Fixtures;

internal static class FixtureTraces
{
    public static IReadOnlyList<(Trace Trace, IReadOnlyList<LogRecord> Logs)> Build()
    {
        var t0 = DateTimeOffset.UtcNow.AddSeconds(-30);
        var orders = ServiceName.From("orders-api");
        var payment = ServiceName.From("payment-service");
        var notify = ServiceName.From("notification-service");

        var crossService = BuildCrossServiceCheckout(t0, orders, payment, notify);
        var singleService = BuildSingleService(t0.AddSeconds(15), orders);
        var withError = BuildErrorTrace(t0.AddSeconds(22), payment);

        return [crossService, singleService, withError];
    }

    private static (Trace, IReadOnlyList<LogRecord>) BuildCrossServiceCheckout(
        DateTimeOffset t0, ServiceName orders, ServiceName payment, ServiceName notify)
    {
        var traceId = TraceId.Parse("4bf92f3577b34da6a3ce929d0e0e4736");

        var rootId = SpanId.Parse("00f067aa0ba902b7");
        var chargeId = SpanId.Parse("a1b2c3d4e5f60718");
        var notifyId = SpanId.Parse("c0ffee0bad0face1");
        var dbId = SpanId.Parse("deadbeefcafebabe");

        var root = Span.Create(
            traceId, rootId, parentSpanId: null,
            orders, "POST /api/checkout",
            SpanKind.Server, SpanStatus.Ok,
            t0, t0.AddMilliseconds(480),
            TelemetryAttributes.From(new Dictionary<string, AttributeValue>
            {
                ["http.method"] = new AttributeValue.Text("POST"),
                ["http.route"] = new AttributeValue.Text("/api/checkout"),
                ["http.status_code"] = new AttributeValue.Integer(200),
                ["user.id"] = new AttributeValue.Integer(42),
                ["cart.total_usd"] = new AttributeValue.Number(129.40),
            }));

        var charge = Span.Create(
            traceId, chargeId, parentSpanId: rootId,
            payment, "Charge.Authorize",
            SpanKind.Internal, SpanStatus.Ok,
            t0.AddMilliseconds(40), t0.AddMilliseconds(300),
            TelemetryAttributes.From(new Dictionary<string, AttributeValue>
            {
                ["payment.method"] = new AttributeValue.Text("card"),
                ["payment.charge_id"] = new AttributeValue.Text("ch_017"),
                ["payment.amount"] = new AttributeValue.Number(129.40),
                ["payment.currency"] = new AttributeValue.Text("USD"),
            }));

        var db = Span.Create(
            traceId, dbId, parentSpanId: chargeId,
            payment, "INSERT payments",
            SpanKind.Client, SpanStatus.Ok,
            t0.AddMilliseconds(120), t0.AddMilliseconds(200),
            TelemetryAttributes.From(new Dictionary<string, AttributeValue>
            {
                ["db.system"] = new AttributeValue.Text("postgresql"),
                ["db.name"] = new AttributeValue.Text("payments"),
                ["db.statement"] = new AttributeValue.Text("INSERT INTO payments (id, amount, currency) VALUES ($1, $2, $3)"),
            }));

        var notification = Span.Create(
            traceId, notifyId, parentSpanId: rootId,
            notify, "email.send",
            SpanKind.Producer, SpanStatus.Ok,
            t0.AddMilliseconds(320), t0.AddMilliseconds(470),
            TelemetryAttributes.From(new Dictionary<string, AttributeValue>
            {
                ["messaging.system"] = new AttributeValue.Text("smtp"),
                ["messaging.destination"] = new AttributeValue.Text("noreply@example.com"),
                ["template"] = new AttributeValue.Text("order_confirmation"),
            }));

        var trace = Trace.Empty(traceId);
        trace.Record(root);
        trace.Record(charge);
        trace.Record(db);
        trace.Record(notification);

        var logs = new List<LogRecord>
        {
            LogRecord.Create(t0.AddMilliseconds(5), LogSeverity.Info, orders,
                "received checkout request user=42 cart=$129.40",
                traceId, rootId),
            LogRecord.Create(t0.AddMilliseconds(45), LogSeverity.Debug, payment,
                "authorizing charge id=ch_017 amount=129.40 currency=USD",
                traceId, chargeId),
            LogRecord.Create(t0.AddMilliseconds(310), LogSeverity.Info, notify,
                "queued email order_id=5081",
                traceId, notifyId),
        };

        return (trace, logs);
    }

    private static (Trace, IReadOnlyList<LogRecord>) BuildSingleService(DateTimeOffset t0, ServiceName service)
    {
        var traceId = TraceId.Parse("1111111111111111aaaaaaaaaaaaaaaa");
        var rootId = SpanId.Parse("1111111111111111");
        var childId = SpanId.Parse("2222222222222222");

        var root = Span.Create(
            traceId, rootId, parentSpanId: null,
            service, "GET /api/health",
            SpanKind.Server, SpanStatus.Ok,
            t0, t0.AddMilliseconds(12));

        var child = Span.Create(
            traceId, childId, parentSpanId: rootId,
            service, "db.ping",
            SpanKind.Client, SpanStatus.Ok,
            t0.AddMilliseconds(1), t0.AddMilliseconds(8));

        var trace = Trace.Empty(traceId);
        trace.Record(root);
        trace.Record(child);

        var logs = new List<LogRecord>
        {
            LogRecord.Create(t0.AddMilliseconds(1), LogSeverity.Debug, service,
                "health probe", traceId, rootId),
        };

        return (trace, logs);
    }

    private static (Trace, IReadOnlyList<LogRecord>) BuildErrorTrace(DateTimeOffset t0, ServiceName service)
    {
        var traceId = TraceId.Parse("ffffffffffffffff1234567890abcdef");
        var rootId = SpanId.Parse("aaaaaaaaaaaaaaaa");

        var root = Span.Create(
            traceId, rootId, parentSpanId: null,
            service, "Charge.Capture",
            SpanKind.Internal,
            SpanStatus.Error("upstream connection refused"),
            t0, t0.AddMilliseconds(2100),
            TelemetryAttributes.From(new Dictionary<string, AttributeValue>
            {
                ["payment.charge_id"] = new AttributeValue.Text("ch_018"),
                ["retry.attempts"] = new AttributeValue.Integer(3),
                ["error.type"] = new AttributeValue.Text("UpstreamConnectionRefused"),
                ["error.message"] = new AttributeValue.Text("no route to host"),
            }));

        var trace = Trace.Empty(traceId);
        trace.Record(root);

        var logs = new List<LogRecord>
        {
            LogRecord.Create(t0.AddMilliseconds(50), LogSeverity.Info, service,
                "starting capture attempt 1/3", traceId, rootId),
            LogRecord.Create(t0.AddMilliseconds(750), LogSeverity.Warn, service,
                "retry 2/3 after timeout 700ms", traceId, rootId),
            LogRecord.Create(t0.AddMilliseconds(2080), LogSeverity.Error, service,
                "capture failed: upstream connection refused (no route to host)",
                traceId, rootId),
        };

        return (trace, logs);
    }
}
