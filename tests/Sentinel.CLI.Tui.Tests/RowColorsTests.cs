using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Logs;
using Sentinel.CLI.Domain.Telemetry.Spans;
using Sentinel.CLI.Tui.Views;
using Terminal.Gui.Drawing;

namespace Sentinel.CLI.Tui.Tests;

public class RowColorsTests
{
    [Theory]
    [InlineData(LogSeverity.Error, ColorName16.BrightRed)]
    [InlineData(LogSeverity.Fatal, ColorName16.BrightRed)]
    [InlineData(LogSeverity.Warn, ColorName16.BrightYellow)]
    [InlineData(LogSeverity.Debug, ColorName16.DarkGray)]
    [InlineData(LogSeverity.Trace, ColorName16.DarkGray)]
    public void ForSeverity_colors_notable_levels(LogSeverity severity, ColorName16 expected)
        => RowColors.ForSeverity(severity).Should().Be(expected);

    [Fact]
    public void ForSeverity_leaves_info_at_default()
        => RowColors.ForSeverity(LogSeverity.Info).Should().BeNull();

    [Fact]
    public void ForStatus_colors_error_red()
        => RowColors.ForStatus(SpanStatusCode.Error).Should().Be(ColorName16.BrightRed);

    [Theory]
    [InlineData(SpanStatusCode.Ok)]
    [InlineData(SpanStatusCode.Unset)]
    public void ForStatus_leaves_non_error_at_default(SpanStatusCode status)
        => RowColors.ForStatus(status).Should().BeNull();

    [Theory]
    [InlineData(SpanStatusCode.Error, ColorName16.BrightRed)]
    [InlineData(SpanStatusCode.Ok, ColorName16.BrightGreen)]
    [InlineData(SpanStatusCode.Unset, ColorName16.Gray)]
    public void StatusToken_colors_the_trace_status(SpanStatusCode status, ColorName16 expected)
        => RowColors.StatusToken(status).Should().Be(expected);
}
