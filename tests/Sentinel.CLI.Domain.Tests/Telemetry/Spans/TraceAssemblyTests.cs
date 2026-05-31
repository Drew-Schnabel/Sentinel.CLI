using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Spans;
using Sentinel.CLI.Domain.Tests.TestHelpers;
using static Sentinel.CLI.Domain.Tests.TestHelpers.SpanBuilder;

namespace Sentinel.CLI.Domain.Tests.Telemetry.Spans;

public class TraceAssemblyTests
{
    private static readonly string A = Sid(1);
    private static readonly string B = Sid(2);
    private static readonly string C = Sid(3);
    private static readonly string D = Sid(4);
    private static readonly string AbsentX = Sid(88);
    private static readonly string AbsentY = Sid(89);
    private static readonly string AbsentZ = Sid(90);

    [Fact]
    public void Assemble_single_root_no_children_returns_single_node_tree()
    {
        var trace = TraceOf(Make(A, startMs: 0));

        var roots = trace.Assemble().Roots;

        roots.Should().HaveCount(1);
        roots[0].Span.SpanId.Value.Should().Be(A);
        roots[0].Children.Should().BeEmpty();
    }

    [Fact]
    public void Assemble_single_root_with_children_returns_correct_depth()
    {
        var trace = TraceOf(
            Make(A, startMs: 0),
            Make(B, parentSpanId: A, startMs: 10),
            Make(C, parentSpanId: A, startMs: 20));

        var roots = trace.Assemble().Roots;

        roots.Should().HaveCount(1);
        roots[0].Children.Select(n => n.Span.SpanId.Value).Should().Equal(B, C);
    }

    [Fact]
    public void Assemble_deep_chain_returns_correct_nesting()
    {
        var trace = TraceOf(
            Make(A, startMs: 0),
            Make(B, parentSpanId: A, startMs: 10),
            Make(C, parentSpanId: B, startMs: 20),
            Make(D, parentSpanId: C, startMs: 30));

        var roots = trace.Assemble().Roots;

        roots.Should().HaveCount(1);
        roots[0].Span.SpanId.Value.Should().Be(A);
        roots[0].Children.Should().ContainSingle().Which.Span.SpanId.Value.Should().Be(B);
        roots[0].Children[0].Children.Should().ContainSingle().Which.Span.SpanId.Value.Should().Be(C);
        roots[0].Children[0].Children[0].Children
            .Should().ContainSingle().Which.Span.SpanId.Value.Should().Be(D);
    }

    [Fact]
    public void Assemble_cross_service_parent_child_ignores_service_boundary()
    {
        var trace = TraceOf(
            Make(A, service: "svc-a", startMs: 0),
            Make(B, parentSpanId: A, service: "svc-b", startMs: 10),
            Make(C, parentSpanId: B, service: "svc-a", startMs: 20));

        var roots = trace.Assemble().Roots;

        roots.Should().HaveCount(1);
        roots[0].Children[0].Span.SpanId.Value.Should().Be(B);
        roots[0].Children[0].Children[0].Span.SpanId.Value.Should().Be(C);
    }

    [Fact]
    public void Assemble_child_arrives_before_parent_still_assembles_correctly()
    {
        // Record child first, then parent — arrival order must not matter.
        var trace = TraceOf(
            Make(B, parentSpanId: A, startMs: 10),
            Make(A, startMs: 0));

        var roots = trace.Assemble().Roots;

        roots.Should().HaveCount(1);
        roots[0].Span.SpanId.Value.Should().Be(A);
        roots[0].Children.Should().ContainSingle().Which.Span.SpanId.Value.Should().Be(B);
    }

    [Fact]
    public void Assemble_multiple_roots_returns_forest_ordered_by_start_time()
    {
        var trace = TraceOf(
            Make(A, startMs: 0),
            Make(B, startMs: 5));

        var roots = trace.Assemble().Roots;

        roots.Select(n => n.Span.SpanId.Value).Should().Equal(A, B);
    }

    [Fact]
    public void Assemble_all_orphans_each_becomes_root()
    {
        var trace = TraceOf(
            Make(A, parentSpanId: AbsentX, startMs: 0),
            Make(B, parentSpanId: AbsentY, startMs: 5));

        var roots = trace.Assemble().Roots;

        roots.Select(n => n.Span.SpanId.Value).Should().Equal(A, B);
        roots.Should().OnlyContain(n => n.Children.Count == 0);
    }

    [Fact]
    public void Assemble_orphan_parent_never_arrives_orphan_is_root()
    {
        var trace = TraceOf(
            Make(A, startMs: 0),
            Make(B, parentSpanId: A, startMs: 10),
            Make(C, parentSpanId: AbsentZ, startMs: 5));

        var roots = trace.Assemble().Roots;

        // Roots sorted ascending by start: A(0) then C(5). A keeps its child B.
        roots.Select(n => n.Span.SpanId.Value).Should().Equal(A, C);
        roots[0].Children.Should().ContainSingle().Which.Span.SpanId.Value.Should().Be(B);
        roots[1].Children.Should().BeEmpty();
    }

    [Fact]
    public void Assemble_empty_trace_returns_empty_forest()
    {
        var assembled = TraceOf().Assemble();

        assembled.Roots.Should().BeEmpty();
        assembled.SpanCount.Should().Be(0);
    }

    [Fact]
    public void Assemble_duplicate_span_id_last_write_wins()
    {
        var trace = TraceOf(
            Make(A, name: "first", startMs: 0),
            Make(A, name: "second", startMs: 0));

        var roots = trace.Assemble().Roots;

        roots.Should().ContainSingle().Which.Span.Name.Should().Be("second");
    }

    [Fact]
    public void Assemble_cross_service_chain_a_b_a_assembles_linear_depth_3()
    {
        var trace = TraceOf(
            Make(A, service: "svc-a", startMs: 0),
            Make(B, parentSpanId: A, service: "svc-b", startMs: 10),
            Make(C, parentSpanId: B, service: "svc-a", startMs: 20));

        var flat = trace.Assemble().Flatten();

        flat.Select(e => e.Depth).Should().Equal(0, 1, 2);
        flat.Select(e => e.Node.Span.SpanId.Value).Should().Equal(A, B, C);
    }

    [Fact]
    public void Assemble_sibling_order_by_start_time_ascending()
    {
        var trace = TraceOf(
            Make(A, startMs: 0),
            Make(B, parentSpanId: A, startMs: 30),
            Make(C, parentSpanId: A, startMs: 10),
            Make(D, parentSpanId: A, startMs: 20));

        var roots = trace.Assemble().Roots;

        roots[0].Children.Select(n => n.Span.SpanId.Value).Should().Equal(C, D, B);
    }

    [Fact]
    public void Assemble_multiple_roots_with_children_correct_forest()
    {
        var trace = TraceOf(
            Make(A, startMs: 0),
            Make(B, parentSpanId: A, startMs: 5),
            Make(C, startMs: 10),
            Make(D, parentSpanId: C, startMs: 15));

        var roots = trace.Assemble().Roots;

        roots.Select(n => n.Span.SpanId.Value).Should().Equal(A, C);
        roots[0].Children.Should().ContainSingle().Which.Span.SpanId.Value.Should().Be(B);
        roots[1].Children.Should().ContainSingle().Which.Span.SpanId.Value.Should().Be(D);
    }

    [Fact]
    public void Assemble_single_orphan_no_other_spans_orphan_is_only_root()
    {
        var trace = TraceOf(Make(A, parentSpanId: AbsentZ, startMs: 0));

        var roots = trace.Assemble().Roots;

        roots.Should().ContainSingle().Which.Span.SpanId.Value.Should().Be(A);
    }

    [Fact]
    public void Assemble_two_spans_identical_start_time_tie_broken_by_span_id()
    {
        // Both roots share StartTime; ordinal SpanId order must decide: Sid(1) before Sid(2).
        var trace = TraceOf(
            Make(B, startMs: 0),
            Make(A, startMs: 0));

        var roots = trace.Assemble().Roots;

        roots.Select(n => n.Span.SpanId.Value).Should().Equal(A, B);
    }

    [Fact]
    public void Assemble_every_span_appears_exactly_once_completeness_invariant()
    {
        var trace = TraceOf(
            Make(A, startMs: 0),
            Make(B, parentSpanId: A, startMs: 10),
            Make(C, parentSpanId: B, startMs: 20),
            Make(D, parentSpanId: AbsentZ, startMs: 5));

        var assembled = trace.Assemble();
        var ids = assembled.Flatten().Select(e => e.Node.Span.SpanId.Value).ToList();

        assembled.SpanCount.Should().Be(4);
        ids.Should().BeEquivalentTo([A, B, C, D]);
        ids.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Assemble_cycle_promotes_unreached_spans_and_preserves_completeness()
    {
        // A→B and B→A form a graph cycle: neither is a strict root. Every span must
        // still appear exactly once and the call must terminate.
        var trace = TraceOf(
            Make(A, parentSpanId: B, startMs: 0),
            Make(B, parentSpanId: A, startMs: 10));

        var assembled = trace.Assemble();
        var ids = assembled.Flatten().Select(e => e.Node.Span.SpanId.Value).ToList();

        assembled.SpanCount.Should().Be(2);
        ids.Should().BeEquivalentTo([A, B]);
        ids.Should().OnlyHaveUniqueItems();
    }
}
