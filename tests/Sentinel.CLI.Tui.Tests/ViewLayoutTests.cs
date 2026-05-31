using FluentAssertions;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class ViewLayoutTests
{
    [Theory]
    [InlineData(PaneId.Traces, true)]
    [InlineData(PaneId.Waterfall, true)]
    [InlineData(PaneId.Details, true)]
    [InlineData(PaneId.Logs, true)]
    [InlineData(PaneId.Metrics, false)] // metrics is a dedicated full-screen view, not in combined
    [InlineData(PaneId.GlobalLogs, false)] // global log stream is also its own full-screen view
    public void Combined_shows_the_four_trace_log_panes(PaneId pane, bool expected)
        => ViewLayout.IsVisible(pane, solo: null).Should().Be(expected);

    [Theory]
    [InlineData(PaneId.Logs)]
    [InlineData(PaneId.Metrics)]
    [InlineData(PaneId.Traces)]
    [InlineData(PaneId.GlobalLogs)]
    public void Solo_shows_only_the_chosen_pane(PaneId solo)
    {
        ViewLayout.IsVisible(solo, solo).Should().BeTrue();
        foreach (var other in Enum.GetValues<PaneId>())
        {
            if (other != solo)
            {
                ViewLayout.IsVisible(other, solo).Should().BeFalse();
            }
        }
    }
}
