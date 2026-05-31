using Sentinel.CLI.Domain.Telemetry.Common;

namespace Sentinel.CLI.Tui.Views;

internal static class TraceSelection
{
    // Which row to select after the trace list is rebuilt: keep the previously selected
    // trace if it is still present, otherwise fall back to the newest (index 0).
    // Returns -1 for an empty list.
    public static int ResolveIndex(IReadOnlyList<TraceSummary> traces, TraceId? previousId)
    {
        ArgumentNullException.ThrowIfNull(traces);
        if (traces.Count == 0)
        {
            return -1;
        }
        if (previousId is { } id)
        {
            for (var i = 0; i < traces.Count; i++)
            {
                if (traces[i].Id.Equals(id))
                {
                    return i;
                }
            }
        }
        return 0;
    }
}
