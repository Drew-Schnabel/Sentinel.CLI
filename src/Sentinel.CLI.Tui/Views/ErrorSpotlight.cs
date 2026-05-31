using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Spans;

namespace Sentinel.CLI.Tui.Views;

// Pulls the "what failed" essentials to the top of the Details pane: the error status message and
// any OTel `exception.*` attributes — recorded either directly on the span or (per OTel convention)
// on an "exception" span event. Pure + unit-tested; returns no lines when there's nothing to show.
internal static class ErrorSpotlight
{
    private const int MaxStackLines = 3;

    public static IReadOnlyList<string> For(Span span)
    {
        ArgumentNullException.ThrowIfNull(span);

        var exception = CollectExceptionAttributes(span);
        if (span.Status.Code != SpanStatusCode.Error && exception.Count == 0)
        {
            return [];
        }

        var lines = new List<string> { "*** ERROR ***" };
        if (!string.IsNullOrEmpty(span.Status.Description))
        {
            lines.Add($"  {span.Status.Description}");
        }

        AddIfPresent(lines, exception, "exception.type", "type");
        AddIfPresent(lines, exception, "exception.message", "message");
        AddStacktrace(lines, exception);

        // Any other exception.* keys we didn't explicitly format (e.g. exception.escaped).
        foreach (var (key, value) in exception)
        {
            if (key is "exception.type" or "exception.message" or "exception.stacktrace")
            {
                continue;
            }
            lines.Add($"  {key["exception.".Length..]}: {AttributeText.Render(value)}");
        }

        return lines;
    }

    private static Dictionary<string, AttributeValue> CollectExceptionAttributes(Span span)
    {
        var map = new Dictionary<string, AttributeValue>(StringComparer.Ordinal);
        ScanInto(map, span.Attributes);
        foreach (var spanEvent in span.Events)
        {
            ScanInto(map, spanEvent.Attributes); // events usually carry the richest exception set
        }
        return map;
    }

    private static void ScanInto(Dictionary<string, AttributeValue> map, TelemetryAttributes attributes)
    {
        foreach (var (key, value) in attributes)
        {
            if (key.StartsWith("exception.", StringComparison.Ordinal))
            {
                map[key] = value; // last wins
            }
        }
    }

    private static void AddIfPresent(
        List<string> lines, Dictionary<string, AttributeValue> exception, string key, string label)
    {
        if (exception.TryGetValue(key, out var value))
        {
            lines.Add($"  {label}: {AttributeText.Render(value)}");
        }
    }

    private static void AddStacktrace(List<string> lines, Dictionary<string, AttributeValue> exception)
    {
        if (!exception.TryGetValue("exception.stacktrace", out var value))
        {
            return;
        }
        var stack = AttributeText.Render(value).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Add("  stacktrace:");
        foreach (var frame in stack.Take(MaxStackLines))
        {
            lines.Add($"    {frame.TrimEnd()}");
        }
        if (stack.Length > MaxStackLines)
        {
            lines.Add($"    … ({stack.Length - MaxStackLines} more lines)");
        }
    }
}
