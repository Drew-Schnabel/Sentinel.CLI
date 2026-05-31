using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Spans;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class TraceDoctorTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static Span MakeSpan(
        char id, char? parent, string service = "svc", string name = "op",
        SpanStatus? status = null, int startMs = 0, int durMs = 10,
        TelemetryAttributes? attributes = null, IReadOnlyList<SpanEvent>? events = null)
        => Span.Create(
            TraceId.Parse(new string('a', 32)),
            SpanId.Parse(new string(id, 16)),
            parent is { } p ? SpanId.Parse(new string(p, 16)) : null,
            ServiceName.From(service),
            name,
            SpanKind.Internal,
            status ?? SpanStatus.Ok,
            Epoch.AddMilliseconds(startMs),
            Epoch.AddMilliseconds(startMs + durMs),
            attributes,
            events);

    private static TelemetryAttributes Attrs(params (string Key, string Value)[] pairs)
        => TelemetryAttributes.From(
            pairs.ToDictionary(p => p.Key, p => (AttributeValue)new AttributeValue.Text(p.Value), StringComparer.Ordinal));

    [Fact]
    public void Diagnose_healthy_trace_finds_nothing()
    {
        var spans = new[]
        {
            MakeSpan('1', null, startMs: 0, durMs: 100),
            MakeSpan('2', '1', startMs: 5, durMs: 50),
        };

        TraceDoctor.Diagnose(spans).Should().BeEmpty();
    }

    [Fact]
    public void Diagnose_empty_finds_nothing()
        => TraceDoctor.Diagnose([]).Should().BeEmpty();

    // The advisor's key case: an incomplete/in-flight trace legitimately has orphans, so the
    // finding must be worded as a POSSIBILITY (it may just be arriving/evicted), not a verdict.
    [Fact]
    public void Diagnose_orphan_span_is_flagged_tentatively()
    {
        var spans = new[]
        {
            MakeSpan('1', null),
            MakeSpan('2', 'f'), // parent 'f' is not in the trace
        };

        var findings = TraceDoctor.Diagnose(spans);

        var orphan = findings.Should().ContainSingle(f => f.Contains("parent not in this trace")).Subject;
        orphan.Should().MatchRegex("hasn't arrived|evicted"); // tentative, not a verdict
    }

    [Fact]
    public void Diagnose_multiple_roots_are_flagged()
    {
        var spans = new[] { MakeSpan('1', null), MakeSpan('2', null) };

        TraceDoctor.Diagnose(spans).Should().Contain(f => f.Contains("no parent"));
    }

    [Fact]
    public void Diagnose_flags_a_child_that_starts_before_its_parent()
    {
        var spans = new[]
        {
            MakeSpan('1', null, startMs: 100, durMs: 100),
            MakeSpan('2', '1', startMs: 50, durMs: 10), // starts 50ms before its parent
        };

        TraceDoctor.Diagnose(spans).Should().Contain(f => f.Contains("skew") && f.Contains("50ms"));
    }

    [Fact]
    public void Diagnose_ignores_a_child_at_the_same_start_as_its_parent()
    {
        var spans = new[]
        {
            MakeSpan('1', null, startMs: 100, durMs: 100),
            MakeSpan('2', '1', startMs: 100, durMs: 10),
        };

        TraceDoctor.Diagnose(spans).Should().NotContain(f => f.Contains("skew"));
    }

    [Fact]
    public void Diagnose_flags_an_exception_without_error_status()
    {
        var ok = MakeSpan('1', null, status: SpanStatus.Ok, attributes: Attrs(("exception.type", "Boom")));
        TraceDoctor.Diagnose([ok]).Should().Contain(f => f.Contains("exception") && f.Contains("status isn't Error"));

        // …but not when the span is correctly marked Error.
        var err = MakeSpan('1', null, status: SpanStatus.Error("boom"), attributes: Attrs(("exception.type", "Boom")));
        TraceDoctor.Diagnose([err]).Should().NotContain(f => f.Contains("status isn't Error"));
    }

    [Fact]
    public void Diagnose_flags_spans_missing_service_name()
    {
        var spans = new[] { MakeSpan('1', null, service: "unknown") };

        TraceDoctor.Diagnose(spans).Should().Contain(f => f.Contains("no service.name"));
    }
}
