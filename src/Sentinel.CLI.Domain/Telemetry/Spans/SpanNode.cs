namespace Sentinel.CLI.Domain.Telemetry.Spans;

public sealed class SpanNode
{
    public Span Span { get; }

    // Children ordered by StartTime ascending, ties broken by SpanId ordinal order.
    public IReadOnlyList<SpanNode> Children { get; }

    public SpanNode(Span span, IReadOnlyList<SpanNode> children)
    {
        ArgumentNullException.ThrowIfNull(span);
        ArgumentNullException.ThrowIfNull(children);
        Span = span;
        Children = children;
    }
}
