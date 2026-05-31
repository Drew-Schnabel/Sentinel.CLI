using FluentAssertions;
using Sentinel.CLI.Tui;

namespace Sentinel.CLI.Tui.Tests;

public class TerminalGuardTests
{
    [Theory]
    [InlineData(false, false, true)]  // real terminal — launch
    [InlineData(true, false, false)]  // stdout piped — refuse
    [InlineData(false, true, false)]  // stdin redirected — refuse
    [InlineData(true, true, false)]   // both redirected — refuse
    public void IsInteractive_requires_both_streams_attached(
        bool outputRedirected, bool inputRedirected, bool expected)
    {
        TerminalGuard.IsInteractive(outputRedirected, inputRedirected).Should().Be(expected);
    }
}
