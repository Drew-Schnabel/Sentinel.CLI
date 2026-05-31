using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Logs;
using Sentinel.CLI.Domain.Telemetry.Metrics;
using Sentinel.CLI.Domain.Telemetry.Spans;
using Sentinel.CLI.Receiver.Telemetry;
using Sentinel.CLI.Receiver.Tests.TestSupport;
using OtlpCommon = OpenTelemetry.Proto.Common.V1;
using OtlpLogs = OpenTelemetry.Proto.Logs.V1;
using OtlpResource = OpenTelemetry.Proto.Resource.V1;
using OtlpTrace = OpenTelemetry.Proto.Trace.V1;

namespace Sentinel.CLI.Receiver.Tests;

public class OtlpMapperTests
{
    [Fact]
    public void MapTraces_valid_span_preserves_core_fields()
    {
        var request = Proto.TraceRequest("checkout", Proto.Span(
            traceId: Proto.TraceIdBytes(0xAB),
            spanId: Proto.SpanIdBytes(0x07),
            name: "POST /checkout",
            kind: OtlpTrace.Span.Types.SpanKind.Server,
            statusCode: OtlpTrace.Status.Types.StatusCode.Ok));

        var (spans, rejected) = OtlpMapper.MapTraces(request.ResourceSpans);

        rejected.Should().Be(0);
        var span = spans.Should().ContainSingle().Subject;
        span.TraceId.Value.Should().Be("000000000000000000000000000000ab");
        span.SpanId.Value.Should().Be("0000000000000007");
        span.Name.Should().Be("POST /checkout");
        span.Service.Value.Should().Be("checkout");
        span.Kind.Should().Be(SpanKind.Server);
        span.Status.Code.Should().Be(SpanStatusCode.Ok);
        span.StartTime.Should().Be(DateTimeOffset.UnixEpoch.AddSeconds(1));
    }

    [Fact]
    public void MapTraces_root_span_with_empty_parent_maps_to_null_parent()
    {
        // OTLP sends a zero-length byte string for root spans. This must become null,
        // not a parse error — otherwise every root span would be silently dropped.
        var request = Proto.TraceRequest(Proto.Span(parentSpanId: null));

        var (spans, rejected) = OtlpMapper.MapTraces(request.ResourceSpans);

        rejected.Should().Be(0);
        spans.Should().ContainSingle().Which.ParentSpanId.Should().BeNull();
    }

    [Fact]
    public void MapTraces_child_span_maps_parent_span_id()
    {
        var request = Proto.TraceRequest(Proto.Span(
            spanId: Proto.SpanIdBytes(0x02),
            parentSpanId: Proto.SpanIdBytes(0x01)));

        var span = OtlpMapper.MapTraces(request.ResourceSpans).Items.Single();

        span.ParentSpanId.Should().NotBeNull();
        span.ParentSpanId!.Value.Value.Should().Be("0000000000000001");
    }

    [Fact]
    public void MapTraces_missing_service_name_uses_unknown()
    {
        var resourceSpans = new OtlpTrace.ResourceSpans { ScopeSpans = { new OtlpTrace.ScopeSpans() } };
        resourceSpans.ScopeSpans[0].Spans.Add(Proto.Span());

        var span = OtlpMapper.MapTraces([resourceSpans]).Items.Single();

        span.Service.Value.Should().Be("unknown");
    }

    [Fact]
    public void MapTraces_attributes_map_each_scalar_arm()
    {
        var protoSpan = Proto.Span();
        protoSpan.Attributes.Add(Proto.Attr("s", new OtlpCommon.AnyValue { StringValue = "txt" }));
        protoSpan.Attributes.Add(Proto.Attr("i", new OtlpCommon.AnyValue { IntValue = 42 }));
        protoSpan.Attributes.Add(Proto.Attr("d", new OtlpCommon.AnyValue { DoubleValue = 1.5 }));
        protoSpan.Attributes.Add(Proto.Attr("b", new OtlpCommon.AnyValue { BoolValue = true }));
        var array = new OtlpCommon.ArrayValue();
        array.Values.Add(new OtlpCommon.AnyValue { StringValue = "x" });
        array.Values.Add(new OtlpCommon.AnyValue { IntValue = 9 });
        protoSpan.Attributes.Add(Proto.Attr("a", new OtlpCommon.AnyValue { ArrayValue = array }));

        var span = OtlpMapper.MapTraces(Proto.TraceRequest(protoSpan).ResourceSpans).Items.Single();
        var attrs = span.Attributes;

        attrs.TryGet("s", out var s).Should().BeTrue();
        s.Should().BeOfType<AttributeValue.Text>().Which.Value.Should().Be("txt");
        attrs.TryGet("i", out var i).Should().BeTrue();
        i.Should().BeOfType<AttributeValue.Integer>().Which.Value.Should().Be(42);
        attrs.TryGet("d", out var d).Should().BeTrue();
        d.Should().BeOfType<AttributeValue.Number>().Which.Value.Should().Be(1.5);
        attrs.TryGet("b", out var b).Should().BeTrue();
        b.Should().BeOfType<AttributeValue.Flag>().Which.Value.Should().BeTrue();
        attrs.TryGet("a", out var a).Should().BeTrue();
        a.Should().BeOfType<AttributeValue.TextList>().Which.Values.Should().Equal("x", "9");
    }

    [Fact]
    public void MapTraces_empty_span_name_is_rejected()
    {
        var request = Proto.TraceRequest(Proto.Span(name: ""));

        var (spans, rejected) = OtlpMapper.MapTraces(request.ResourceSpans);

        spans.Should().BeEmpty();
        rejected.Should().Be(1);
    }

    [Fact]
    public void MapTraces_end_before_start_is_rejected()
    {
        var request = Proto.TraceRequest(Proto.Span(startNanos: 2_000_000_000, endNanos: 1_000_000_000));

        OtlpMapper.MapTraces(request.ResourceSpans).Rejected.Should().Be(1);
    }

    [Fact]
    public void MapTraces_all_zero_trace_id_is_rejected()
    {
        var request = Proto.TraceRequest(Proto.Span(traceId: new byte[16]));

        OtlpMapper.MapTraces(request.ResourceSpans).Rejected.Should().Be(1);
    }

    [Fact]
    public void MapTraces_wrong_length_span_id_is_rejected()
    {
        var request = Proto.TraceRequest(Proto.Span(spanId: new byte[4]));

        OtlpMapper.MapTraces(request.ResourceSpans).Rejected.Should().Be(1);
    }

    [Fact]
    public void MapTraces_one_bad_span_does_not_lose_the_good_ones()
    {
        var good = Proto.Span(spanId: Proto.SpanIdBytes(0x01));
        var bad = Proto.Span(spanId: Proto.SpanIdBytes(0x02), name: "");

        var (spans, rejected) = OtlpMapper.MapTraces(Proto.TraceRequest(good, bad).ResourceSpans);

        spans.Should().ContainSingle();
        rejected.Should().Be(1);
    }

    [Fact]
    public void MapTraces_multiple_resource_spans_all_mapped()
    {
        var a = Proto.TraceRequest("svc-a", Proto.Span(spanId: Proto.SpanIdBytes(0x01))).ResourceSpans[0];
        var b = Proto.TraceRequest("svc-b", Proto.Span(spanId: Proto.SpanIdBytes(0x02))).ResourceSpans[0];

        var (spans, _) = OtlpMapper.MapTraces([a, b]);

        spans.Should().HaveCount(2);
        spans.Select(s => s.Service.Value).Should().BeEquivalentTo(["svc-a", "svc-b"]);
    }

    [Fact]
    public void MapTraces_span_events_are_mapped()
    {
        var protoSpan = Proto.Span();
        protoSpan.Events.Add(new OtlpTrace.Span.Types.Event { TimeUnixNano = 1_000_000_000, Name = "exception" });

        var span = OtlpMapper.MapTraces(Proto.TraceRequest(protoSpan).ResourceSpans).Items.Single();

        span.Events.Should().ContainSingle().Which.Name.Should().Be("exception");
    }

    [Fact]
    public void MapTraces_span_links_are_mapped()
    {
        var protoSpan = Proto.Span();
        protoSpan.Links.Add(new OtlpTrace.Span.Types.Link
        {
            TraceId = Google.Protobuf.ByteString.CopyFrom(Proto.TraceIdBytes(0x10)),
            SpanId = Google.Protobuf.ByteString.CopyFrom(Proto.SpanIdBytes(0x11)),
        });

        var span = OtlpMapper.MapTraces(Proto.TraceRequest(protoSpan).ResourceSpans).Items.Single();

        span.Links.Should().ContainSingle();
    }

    [Fact]
    public void MapTraces_malformed_event_is_skipped_but_span_kept()
    {
        var protoSpan = Proto.Span();
        protoSpan.Events.Add(new OtlpTrace.Span.Types.Event { TimeUnixNano = 1, Name = "" }); // blank → skipped

        var (spans, rejected) = OtlpMapper.MapTraces(Proto.TraceRequest(protoSpan).ResourceSpans);

        rejected.Should().Be(0);
        spans.Should().ContainSingle().Which.Events.Should().BeEmpty();
    }

    [Fact]
    public void MapTraces_link_with_invalid_target_is_skipped_but_span_kept()
    {
        var protoSpan = Proto.Span();
        protoSpan.Links.Add(new OtlpTrace.Span.Types.Link
        {
            TraceId = Google.Protobuf.ByteString.CopyFrom(new byte[4]),
            SpanId = Google.Protobuf.ByteString.CopyFrom(new byte[2]),
        });

        var span = OtlpMapper.MapTraces(Proto.TraceRequest(protoSpan).ResourceSpans).Items.Single();

        span.Links.Should().BeEmpty();
    }

    [Fact]
    public void MapTraces_folds_selected_resource_attributes_with_prefix()
    {
        var resource = new OtlpResource.Resource
        {
            Attributes =
            {
                new OtlpCommon.KeyValue { Key = "service.name", Value = new OtlpCommon.AnyValue { StringValue = "svc" } },
                new OtlpCommon.KeyValue { Key = "host.name", Value = new OtlpCommon.AnyValue { StringValue = "box-1" } },
            },
        };
        var resourceSpans = new OtlpTrace.ResourceSpans { Resource = resource, ScopeSpans = { new OtlpTrace.ScopeSpans() } };
        resourceSpans.ScopeSpans[0].Spans.Add(Proto.Span());

        var span = OtlpMapper.MapTraces([resourceSpans]).Items.Single();

        span.Attributes.TryGet("resource.host.name", out var host).Should().BeTrue();
        host.Should().BeOfType<AttributeValue.Text>().Which.Value.Should().Be("box-1");
        // service.name becomes the Service, not a folded attribute.
        span.Attributes.TryGet("resource.service.name", out _).Should().BeFalse();
        span.Service.Value.Should().Be("svc");
    }

    [Fact]
    public void MapLogs_captures_severity_text()
    {
        var record = new OtlpLogs.LogRecord
        {
            TimeUnixNano = 1_000_000_000,
            SeverityNumber = OtlpLogs.SeverityNumber.Warn,
            SeverityText = "WARNING",
            Body = new OtlpCommon.AnyValue { StringValue = "x" },
        };

        var log = OtlpMapper.MapLogs(Proto.LogRequest(record).ResourceLogs).Items.Single();

        log.SeverityText.Should().Be("WARNING");
    }

    [Fact]
    public void MapMetrics_gauge_maps_to_gauge_point()
    {
        var (items, rejected) = OtlpMapper.MapMetrics(Proto.MetricRequest(Proto.Gauge("cpu.usage", 0.5)).ResourceMetrics);

        rejected.Should().Be(0);
        var metric = items.Should().ContainSingle().Subject;
        metric.Kind.Should().Be(MetricKind.Gauge);
        metric.Name.Should().Be("cpu.usage");
        metric.Value.Should().Be(0.5);
    }

    [Fact]
    public void MapMetrics_sum_maps_int_value()
    {
        var metric = OtlpMapper.MapMetrics(Proto.MetricRequest(Proto.Sum("requests", 42)).ResourceMetrics).Items.Single();

        metric.Kind.Should().Be(MetricKind.Sum);
        metric.Value.Should().Be(42);
    }

    [Fact]
    public void MapMetrics_histogram_maps_count_and_sum()
    {
        var metric = OtlpMapper.MapMetrics(
            Proto.MetricRequest(Proto.Histogram("latency", count: 10, sum: 100, min: 1, max: 50)).ResourceMetrics)
            .Items.Single();

        metric.Kind.Should().Be(MetricKind.Histogram);
        metric.Count.Should().Be(10);
        metric.Sum.Should().Be(100);
        metric.Max.Should().Be(50);
    }

    [Fact]
    public void MapMetrics_unsupported_kind_is_rejected()
    {
        var (items, rejected) = OtlpMapper.MapMetrics(Proto.MetricRequest(Proto.Summary("p99")).ResourceMetrics);

        items.Should().BeEmpty();
        rejected.Should().Be(1);
    }

    [Fact]
    public void MapLogs_valid_log_maps_severity_body_and_correlation()
    {
        var record = new OtlpLogs.LogRecord
        {
            TimeUnixNano = 1_000_000_000,
            SeverityNumber = OtlpLogs.SeverityNumber.Error,
            Body = new OtlpCommon.AnyValue { StringValue = "boom" },
            TraceId = Google.Protobuf.ByteString.CopyFrom(Proto.TraceIdBytes(0x05)),
            SpanId = Google.Protobuf.ByteString.CopyFrom(Proto.SpanIdBytes(0x06)),
        };

        var log = OtlpMapper.MapLogs(Proto.LogRequest(record).ResourceLogs).Items.Single();

        log.Severity.Should().Be(LogSeverity.Error);
        log.Body.Should().Be("boom");
        log.TraceId.Should().NotBeNull();
        log.SpanId!.Value.Value.Should().Be("0000000000000006");
    }

    [Fact]
    public void MapLogs_log_without_ids_has_null_correlation()
    {
        var record = new OtlpLogs.LogRecord
        {
            TimeUnixNano = 1_000_000_000,
            SeverityNumber = OtlpLogs.SeverityNumber.Info,
            Body = new OtlpCommon.AnyValue { StringValue = "hi" },
        };

        var log = OtlpMapper.MapLogs(Proto.LogRequest(record).ResourceLogs).Items.Single();

        log.TraceId.Should().BeNull();
        log.SpanId.Should().BeNull();
    }
}
