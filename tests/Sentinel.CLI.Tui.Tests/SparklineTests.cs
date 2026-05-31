using FluentAssertions;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class SparklineTests
{
    [Fact]
    public void Empty_input_renders_nothing()
        => Sparkline.Render([]).Should().BeEmpty();

    [Fact]
    public void One_glyph_per_value()
        => Sparkline.Render([1, 2, 3, 4]).Should().HaveLength(4);

    [Fact]
    public void Ascending_run_goes_from_lowest_bar_to_highest()
    {
        var spark = Sparkline.Render([0, 25, 50, 75, 100]);

        spark[0].Should().Be('▁');   // the minimum
        spark[^1].Should().Be('█');  // the maximum
    }

    [Fact]
    public void Flat_run_renders_a_steady_mid_bar_not_zero()
    {
        var spark = Sparkline.Render([5, 5, 5]);

        spark.Should().Be("▄▄▄"); // mid bar, so a constant non-zero series doesn't look empty
    }
}
