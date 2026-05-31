namespace Sentinel.CLI.Domain.Telemetry.Spans;

public sealed class AssembledTrace
{
    public static AssembledTrace Empty { get; } = new(Array.Empty<SpanNode>());

    // All root-level nodes, ordered by StartTime ascending, ties broken by SpanId ordinal order.
    public IReadOnlyList<SpanNode> Roots { get; }

    // Count of every span represented across all roots and their subtrees.
    public int SpanCount { get; }

    // Wall-clock envelope: min(StartTime) and max(EndTime) across all spans.
    // (DateTimeOffset.MinValue, DateTimeOffset.MinValue) when empty.
    public (DateTimeOffset TraceStart, DateTimeOffset TraceEnd) Envelope { get; }

    public AssembledTrace(IReadOnlyList<SpanNode> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        Roots = roots;

        var count = 0;
        DateTimeOffset? min = null;
        DateTimeOffset? max = null;

        var stack = new Stack<SpanNode>();
        for (var i = roots.Count - 1; i >= 0; i--)
        {
            stack.Push(roots[i]);
        }
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            count++;
            var span = node.Span;
            if (min is null || span.StartTime < min.Value)
            {
                min = span.StartTime;
            }
            if (max is null || span.EndTime > max.Value)
            {
                max = span.EndTime;
            }
            foreach (var child in node.Children)
            {
                stack.Push(child);
            }
        }

        SpanCount = count;
        Envelope = count == 0
            ? (DateTimeOffset.MinValue, DateTimeOffset.MinValue)
            : (min!.Value, max!.Value);
    }

    // Depth-first (pre-order) flattening of Roots and their subtrees.
    // Each entry carries its depth for indent-based rendering.
    public IReadOnlyList<(SpanNode Node, int Depth)> Flatten()
    {
        var result = new List<(SpanNode Node, int Depth)>(SpanCount);
        var stack = new Stack<(SpanNode Node, int Depth)>();
        for (var i = Roots.Count - 1; i >= 0; i--)
        {
            stack.Push((Roots[i], 0));
        }
        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();
            result.Add((node, depth));
            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push((node.Children[i], depth + 1));
            }
        }
        return result;
    }
}
