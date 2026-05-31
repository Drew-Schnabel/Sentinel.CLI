using FluentAssertions;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class StatusLineTests
{
    [Fact]
    public void Format_shows_counts_and_omits_dropped_when_none()
    {
        var line = StatusLine.Format(traces: 3, logs: 5, metrics: 2, 0, 0, 0);

        line.Should().Contain("traces 3").And.Contain("logs 5").And.Contain("metrics 2");
        line.Should().NotContain("dropped");
    }

    [Fact]
    public void Format_includes_dropped_breakdown_when_any()
    {
        var line = StatusLine.Format(traces: 1, logs: 1, metrics: 0, droppedSpans: 2, droppedLogs: 3, droppedMetrics: 0);

        line.Should().Contain("dropped 2s/3l/0m");
    }

    [Fact]
    public void Format_shows_paused_indicator_only_when_paused()
    {
        StatusLine.Format(1, 1, 0, 0, 0, 0, paused: false).Should().NotContain("PAUSED");
        StatusLine.Format(1, 1, 0, 0, 0, 0, paused: true).Should().Contain("PAUSED");
    }

    [Fact]
    public void Format_appends_a_command_message_alongside_the_counts()
    {
        var line = StatusLine.Format(3, 5, 2, 0, 0, 0, message: "exported ./x.json");

        line.Should().Contain("traces 3");                 // counts still visible
        line.Should().Contain("exported ./x.json");        // …and the echo rides along
    }
}
