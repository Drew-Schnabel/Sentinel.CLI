namespace Sentinel.CLI.Tui.Views;

// Pure formatter for the bottom status bar. Unit-tested without the TUI.
internal static class StatusLine
{
    public static string Format(
        int traces, int logs, int metrics,
        long droppedSpans, long droppedLogs, long droppedMetrics,
        string? filter = null,
        bool paused = false,
        string? message = null)
    {
        var prefix = paused ? " [PAUSED]  ·  " : " ";
        var line = $"{prefix}traces {traces}  ·  logs {logs}  ·  metrics {metrics}";
        if (droppedSpans + droppedLogs + droppedMetrics > 0)
        {
            line += $"  ·  dropped {droppedSpans}s/{droppedLogs}l/{droppedMetrics}m";
        }
        if (!string.IsNullOrEmpty(filter))
        {
            line += $"  ·  {filter}";
        }
        // A transient command echo rides alongside the counts (so they stay visible) and is held
        // for a few refresh ticks by the caller before it clears.
        if (!string.IsNullOrEmpty(message))
        {
            line += $"   « {message}";
        }
        return line;
    }
}
