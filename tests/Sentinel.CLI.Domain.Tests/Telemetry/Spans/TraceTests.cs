using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Spans;
using static Sentinel.CLI.Domain.Tests.TestHelpers.SpanBuilder;

namespace Sentinel.CLI.Domain.Tests.Telemetry.Spans;

public class TraceTests
{
    private static readonly string A = Sid(1);
    private static readonly string B = Sid(2);
    private static readonly string C = Sid(3);
    private static readonly string AbsentX = Sid(88);
    private static readonly string AbsentY = Sid(89);

    [Fact]
    public void FindRoot_single_root_returns_it()
    {
        var trace = TraceOf(Make(A));

        trace.FindRoot()!.SpanId.Value.Should().Be(A);
    }

    [Fact]
    public void FindRoot_multiple_strict_roots_returns_null()
    {
        var trace = TraceOf(Make(A), Make(B));

        trace.FindRoot().Should().BeNull();
    }

    [Fact]
    public void FindRoot_root_with_children_returns_root()
    {
        var trace = TraceOf(
            Make(A),
            Make(B, parentSpanId: A),
            Make(C, parentSpanId: A));

        trace.FindRoot()!.SpanId.Value.Should().Be(A);
    }

    [Fact]
    public void FindRoot_all_orphans_returns_null()
    {
        var trace = TraceOf(
            Make(A, parentSpanId: AbsentX),
            Make(B, parentSpanId: AbsentY));

        trace.FindRoot().Should().BeNull();
    }

    [Fact]
    public void FindRoot_empty_trace_returns_null()
    {
        TraceOf().FindRoot().Should().BeNull();
    }

    [Fact]
    public void FindRoot_orphan_promoted_to_sole_root_returns_orphan()
    {
        var trace = TraceOf(Make(A, parentSpanId: AbsentX));

        trace.FindRoot()!.SpanId.Value.Should().Be(A);
    }

    [Fact]
    public void Record_duplicate_span_id_overwrites_last_write_wins()
    {
        var trace = TraceOf(
            Make(A, name: "first"),
            Make(A, name: "second"));

        trace.Spans.Should().ContainSingle().Which.Name.Should().Be("second");
    }

    [Fact]
    public void Record_span_from_a_different_trace_throws()
    {
        var trace = Trace.Empty(TraceId.Parse(DefaultTraceId));
        var foreignSpan = Make(A, traceId: "1111111111111111aaaaaaaaaaaaaaaa");

        var act = () => trace.Record(foreignSpan);

        act.Should().Throw<InvalidOperationException>();
    }
}
