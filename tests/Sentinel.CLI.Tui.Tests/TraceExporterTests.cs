using System.Text.Json;
using FluentAssertions;
using Sentinel.CLI.Application.Serialization;
using Sentinel.CLI.Tui.Fixtures;

namespace Sentinel.CLI.Tui.Tests;

public class TraceExporterTests
{
    // The cross-service checkout fixture: orders-api (root) + payment-service + notification-service,
    // 4 spans, 3 logs, with typed attributes (http.status_code int, cart.total_usd double, …).
    private static (Sentinel.CLI.Domain.Telemetry.Spans.Trace Trace,
        IReadOnlyList<Sentinel.CLI.Domain.Telemetry.Logs.LogRecord> Logs) Checkout()
        => FixtureTraces.Build()[0];

    [Fact]
    public void ToJson_writes_trace_id_spans_and_logs()
    {
        var (trace, logs) = Checkout();

        var json = TraceExporter.ToJson(trace.Id, trace.Spans.ToList(), logs);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        root.GetProperty("traceId").GetString().Should().Be(trace.Id.Value);
        root.GetProperty("spans").GetArrayLength().Should().Be(4);
        root.GetProperty("logs").GetArrayLength().Should().Be(3);
    }

    [Fact]
    public void ToJson_includes_every_service_across_the_trace()
    {
        var (trace, logs) = Checkout();

        var json = TraceExporter.ToJson(trace.Id, trace.Spans.ToList(), logs);

        using var doc = JsonDocument.Parse(json);
        var services = doc.RootElement.GetProperty("spans").EnumerateArray()
            .Select(s => s.GetProperty("service").GetString())
            .ToList();
        services.Should().Contain("orders-api")
            .And.Contain("payment-service")
            .And.Contain("notification-service");
    }

    [Fact]
    public void ToJson_flattens_attribute_values_to_their_json_primitive()
    {
        var (trace, logs) = Checkout();

        var json = TraceExporter.ToJson(trace.Id, trace.Spans.ToList(), logs);

        using var doc = JsonDocument.Parse(json);
        var rootSpan = doc.RootElement.GetProperty("spans").EnumerateArray()
            .First(s => s.GetProperty("name").GetString() == "POST /api/checkout");
        var attrs = rootSpan.GetProperty("attributes");

        attrs.GetProperty("http.method").GetString().Should().Be("POST");          // Text -> string
        attrs.GetProperty("http.status_code").GetInt32().Should().Be(200);          // Integer -> number
        attrs.GetProperty("cart.total_usd").GetDouble().Should().BeApproximately(129.40, 0.001); // Number -> number
    }

    [Fact]
    public void ToJson_records_parent_links_and_omits_parent_for_the_root()
    {
        var (trace, logs) = Checkout();

        var json = TraceExporter.ToJson(trace.Id, trace.Spans.ToList(), logs);

        using var doc = JsonDocument.Parse(json);
        var spans = doc.RootElement.GetProperty("spans").EnumerateArray().ToList();
        var root = spans.First(s => s.GetProperty("name").GetString() == "POST /api/checkout");
        root.TryGetProperty("parentSpanId", out _).Should().BeFalse(); // null omitted
        // a child span carries its parent
        var child = spans.First(s => s.GetProperty("name").GetString() == "Charge.Authorize");
        child.GetProperty("parentSpanId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ToJson_round_trips_through_a_file_as_valid_json()
    {
        var (trace, logs) = Checkout();
        var path = Path.Combine(Path.GetTempPath(), $"sentinel-export-test-{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(path, TraceExporter.ToJson(trace.Id, trace.Spans.ToList(), logs));

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            doc.RootElement.GetProperty("traceId").GetString().Should().Be(trace.Id.Value);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
