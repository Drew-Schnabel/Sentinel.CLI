namespace Sentinel.CLI.Tui.Views;

// Pure decision for which panes are visible. Combined view shows the four trace/log panes;
// Metrics and the global log stream are their own full-screen views. A solo view shows only
// the chosen pane.
internal static class ViewLayout
{
    public static bool IsVisible(PaneId pane, PaneId? solo)
        => solo is { } only
            ? pane == only
            : pane is not (PaneId.Metrics or PaneId.GlobalLogs);
}
