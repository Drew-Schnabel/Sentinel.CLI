using FluentAssertions;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class ErrorNavigationTests
{
    [Fact]
    public void NewerErrorIndex_empty_returns_minus_one()
        => ErrorNavigation.NewerErrorIndex([], 0).Should().Be(-1);

    [Fact]
    public void NewerErrorIndex_no_errors_returns_minus_one()
        => ErrorNavigation.NewerErrorIndex([false, false, false], 2).Should().Be(-1);

    [Fact]
    public void NewerErrorIndex_finds_nearest_error_above_current()
        => ErrorNavigation.NewerErrorIndex([false, true, false, false], 3).Should().Be(1);

    [Fact]
    public void NewerErrorIndex_picks_the_closest_newer_not_the_topmost()
        => ErrorNavigation.NewerErrorIndex([true, false, true, false], 3).Should().Be(2);

    [Fact]
    public void NewerErrorIndex_stops_at_top_when_no_newer_error()
        => ErrorNavigation.NewerErrorIndex([false, false, true], 2).Should().Be(-1);

    [Fact]
    public void NewerErrorIndex_at_top_returns_minus_one()
        => ErrorNavigation.NewerErrorIndex([true, false, false], 0).Should().Be(-1);
}
