using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Spans;

namespace Sentinel.CLI.Tui.Views;

// Inspects an assembled trace's spans for common OpenTelemetry instrumentation problems and returns
// human-readable findings (empty = nothing flagged). Pure + unit-tested.
//
// IMPORTANT: the store assembles traces on view over whatever spans have arrived, and FIFO eviction
// can drop a parent while children remain — so a trace that's still being received, or whose parent
// was evicted, legitimately has "orphaned" spans and extra roots. Findings are therefore worded as
// POSSIBILITIES ("…or the parent hasn't arrived / was evicted"), never as verdicts.
internal static class TraceDoctor
{
    private const int MaxPerCategory = 3;
    private static readonly TimeSpan SkewFloor = TimeSpan.FromMilliseconds(1); // ignore rounding noise

    public static IReadOnlyList<string> Diagnose(IReadOnlyList<Span> spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        if (spans.Count == 0)
        {
            return [];
        }

        var present = new HashSet<SpanId>();
        var byId = new Dictionary<SpanId, Span>();
        foreach (var span in spans)
        {
            present.Add(span.SpanId);
            byId[span.SpanId] = span;
        }

        var findings = new List<string>();

        AddStructureFindings(spans, present, findings);
        AddClockSkewFindings(spans, byId, findings);
        AddExceptionStatusFindings(spans, findings);
        AddMissingServiceFindings(spans, findings);

        return findings;
    }

    private static void AddStructureFindings(IReadOnlyList<Span> spans, HashSet<SpanId> present, List<string> findings)
    {
        var orphans = spans.Where(s => s.ParentSpanId is { } p && !present.Contains(p)).ToList();
        var trueRoots = spans.Where(s => s.ParentSpanId is null).ToList();

        if (orphans.Count > 0)
        {
            var sample = orphans[0];
            findings.Add(
                $"{orphans.Count} span(s) reference a parent not in this trace — broken context " +
                $"propagation, or the parent hasn't arrived yet / was evicted " +
                $"(e.g. {sample.Service.Value} \"{sample.Name}\")");
        }

        if (trueRoots.Count > 1)
        {
            findings.Add(
                $"{trueRoots.Count} spans have no parent — possibly a fragmented or merged trace " +
                "(or the entry span is still arriving)");
        }
    }

    private static void AddClockSkewFindings(
        IReadOnlyList<Span> spans, Dictionary<SpanId, Span> byId, List<string> findings)
    {
        var skewed = new List<string>();
        foreach (var span in spans)
        {
            if (span.ParentSpanId is not { } parentId || !byId.TryGetValue(parentId, out var parent))
            {
                continue;
            }
            var before = parent.StartTime - span.StartTime; // child starts before parent
            if (before > SkewFloor)
            {
                skewed.Add(
                    $"span \"{span.Name}\" ({span.Service.Value}) starts " +
                    $"{before.TotalMilliseconds:F0}ms before its parent \"{parent.Name}\" " +
                    $"({parent.Service.Value}) — clock skew between services?");
            }
        }
        AddCapped(findings, skewed, "clock-skew pair(s)");
    }

    private static void AddExceptionStatusFindings(IReadOnlyList<Span> spans, List<string> findings)
    {
        var mismatches = new List<string>();
        foreach (var span in spans)
        {
            if (span.Status.Code != SpanStatusCode.Error && HasExceptionData(span))
            {
                mismatches.Add(
                    $"span \"{span.Name}\" ({span.Service.Value}) recorded an exception but its " +
                    "status isn't Error");
            }
        }
        AddCapped(findings, mismatches, "span(s) with an exception but no error status");
    }

    private static void AddMissingServiceFindings(IReadOnlyList<Span> spans, List<string> findings)
    {
        // "unknown" is the mapper's fallback when the OTLP resource omits service.name.
        var count = spans.Count(s => s.Service.Value == "unknown");
        if (count > 0)
        {
            findings.Add($"{count} span(s) have no service.name (resource is missing it)");
        }
    }

    private static bool HasExceptionData(Span span)
    {
        foreach (var (key, _) in span.Attributes)
        {
            if (key.StartsWith("exception.", StringComparison.Ordinal))
            {
                return true;
            }
        }
        foreach (var spanEvent in span.Events)
        {
            foreach (var (key, _) in spanEvent.Attributes)
            {
                if (key.StartsWith("exception.", StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // Add up to MaxPerCategory of `items`, then a single "(+N more …)" summary for the remainder.
    private static void AddCapped(List<string> findings, List<string> items, string remainderLabel)
    {
        findings.AddRange(items.Take(MaxPerCategory));
        if (items.Count > MaxPerCategory)
        {
            findings.Add($"(+{items.Count - MaxPerCategory} more {remainderLabel})");
        }
    }
}
