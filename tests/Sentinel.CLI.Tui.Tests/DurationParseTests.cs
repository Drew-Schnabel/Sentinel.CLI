using FluentAssertions;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class DurationParseTests
{
    [Theory]
    [InlineData("500ms", 0.5)]
    [InlineData("30s", 30)]
    [InlineData("5m", 300)]
    [InlineData("2h", 7200)]
    [InlineData("1d", 86400)]
    [InlineData("1.5m", 90)]
    [InlineData(" 30S ", 30)] // trimmed + case-insensitive
    public void TryParse_reads_number_and_unit(string text, double expectedSeconds)
        => DurationParse.TryParse(text)!.Value.TotalSeconds.Should().BeApproximately(expectedSeconds, 0.001);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("5x")]    // unknown unit
    [InlineData("abc")]   // no number
    [InlineData("m")]     // no number
    [InlineData("30")]    // no unit
    [InlineData("-5s")]   // negative
    public void TryParse_returns_null_for_malformed_input(string? text)
        => DurationParse.TryParse(text).Should().BeNull();
}
