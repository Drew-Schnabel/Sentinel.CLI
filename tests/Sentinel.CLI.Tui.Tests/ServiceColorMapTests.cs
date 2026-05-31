using FluentAssertions;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class ServiceColorMapTests
{
    [Fact]
    public void Same_service_keeps_its_color_across_calls()
    {
        var map = new ServiceColorMap();
        map.ColorFor("payment-service").Should().Be(map.ColorFor("payment-service"));
    }

    [Fact]
    public void Distinct_services_get_distinct_colors_within_the_palette()
    {
        var map = new ServiceColorMap();
        string[] services =
            ["orders-api", "payment-service", "notification-service", "inventory-service", "warehouse-db"];

        var colors = services.Select(map.ColorFor).ToList();

        colors.Distinct().Should().HaveCount(services.Length);
    }

    [Fact]
    public void Colors_repeat_only_after_the_palette_is_exhausted()
    {
        var map = new ServiceColorMap();
        // 8-color palette: the 9th distinct service wraps to the 1st color.
        var first = map.ColorFor("svc-0");
        for (var i = 1; i < 8; i++)
        {
            map.ColorFor($"svc-{i}");
        }
        map.ColorFor("svc-8").Should().Be(first);
    }
}
