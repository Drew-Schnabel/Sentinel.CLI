using Sentinel.CLI.Domain.Telemetry.Common;

namespace Sentinel.CLI.Domain.Telemetry.Spans;

public sealed class Trace
{
    public TraceId Id { get; }

    private readonly Dictionary<SpanId, Span> _spans = new();

    public IReadOnlyCollection<Span> Spans => _spans.Values;

    private Trace(TraceId id) => Id = id;

    public static Trace Empty(TraceId id) => new(id);

    public void Record(Span span)
    {
        ArgumentNullException.ThrowIfNull(span);
        if (!span.TraceId.Equals(Id))
        {
            throw new InvalidOperationException(
                $"Span trace_id {span.TraceId} does not belong to trace {Id}.");
        }
        _spans[span.SpanId] = span;
    }

    public Span? FindRoot()
    {
        Span? root = null;
        foreach (var span in _spans.Values)
        {
            if (IsRoot(span))
            {
                if (root is not null)
                {
                    return null;
                }
                root = span;
            }
        }
        return root;
    }

    // A span is a root candidate when its parent is absent from this trace:
    // either it has no parent, or the parent span has not (yet) arrived.
    private bool IsRoot(Span span) =>
        span.ParentSpanId is null || !_spans.ContainsKey(span.ParentSpanId.Value);

    // Depth-first, root-ordered assembly of every span in this trace. Tolerates
    // multiple roots, orphans (parent never arrives), and graph cycles. Assembly
    // keys purely on span_id / parent_span_id, never on service — that is what
    // lets spans from different services join one tree. Iterative to stay safe
    // on arbitrarily deep traces.
    public AssembledTrace Assemble()
    {
        if (_spans.Count == 0)
        {
            return AssembledTrace.Empty;
        }

        var childrenByParent = new Dictionary<SpanId, List<Span>>();
        var roots = new List<Span>();
        foreach (var span in _spans.Values)
        {
            if (IsRoot(span))
            {
                roots.Add(span);
            }
            else
            {
                var parent = span.ParentSpanId!.Value;
                if (!childrenByParent.TryGetValue(parent, out var siblings))
                {
                    siblings = [];
                    childrenByParent[parent] = siblings;
                }
                siblings.Add(span);
            }
        }

        foreach (var siblings in childrenByParent.Values)
        {
            siblings.Sort(CompareSpans);
        }

        var visited = new HashSet<SpanId>();
        var rootNodes = new List<SpanNode>(roots.Count);

        roots.Sort(CompareSpans);
        foreach (var root in roots)
        {
            if (visited.Add(root.SpanId))
            {
                rootNodes.Add(BuildSubtree(root, childrenByParent, visited));
            }
        }

        // Completeness: any span not reached from a root is part of a cycle.
        // Promote each such span to a root so every span appears exactly once.
        if (visited.Count < _spans.Count)
        {
            var unreached = new List<Span>();
            foreach (var span in _spans.Values)
            {
                if (!visited.Contains(span.SpanId))
                {
                    unreached.Add(span);
                }
            }
            unreached.Sort(CompareSpans);
            foreach (var span in unreached)
            {
                if (visited.Add(span.SpanId))
                {
                    rootNodes.Add(BuildSubtree(span, childrenByParent, visited));
                }
            }
        }

        rootNodes.Sort(static (a, b) => CompareSpans(a.Span, b.Span));
        return new AssembledTrace(rootNodes);
    }

    private static SpanNode BuildSubtree(
        Span root,
        Dictionary<SpanId, List<Span>> childrenByParent,
        HashSet<SpanId> visited)
    {
        var frames = new Stack<Frame>();
        frames.Push(new Frame(root, ChildrenOf(root, childrenByParent)));
        SpanNode? completed = null;

        while (frames.Count > 0)
        {
            var frame = frames.Peek();
            if (frame.Index < frame.Children.Count)
            {
                var child = frame.Children[frame.Index];
                frame.Index++;
                if (!visited.Add(child.SpanId))
                {
                    continue; // cycle guard — already part of the tree
                }
                frames.Push(new Frame(child, ChildrenOf(child, childrenByParent)));
            }
            else
            {
                var node = new SpanNode(frame.Span, frame.Built);
                frames.Pop();
                if (frames.Count > 0)
                {
                    frames.Peek().Built.Add(node);
                }
                else
                {
                    completed = node;
                }
            }
        }

        return completed!;
    }

    private static IReadOnlyList<Span> ChildrenOf(
        Span span,
        Dictionary<SpanId, List<Span>> childrenByParent) =>
        childrenByParent.TryGetValue(span.SpanId, out var children)
            ? children
            : Array.Empty<Span>();

    private static int CompareSpans(Span a, Span b)
    {
        var byTime = a.StartTime.CompareTo(b.StartTime);
        return byTime != 0
            ? byTime
            : string.CompareOrdinal(a.SpanId.Value, b.SpanId.Value);
    }

    private sealed class Frame(Span span, IReadOnlyList<Span> children)
    {
        public Span Span { get; } = span;
        public IReadOnlyList<Span> Children { get; } = children;
        public int Index { get; set; }
        public List<SpanNode> Built { get; } = [];
    }
}
