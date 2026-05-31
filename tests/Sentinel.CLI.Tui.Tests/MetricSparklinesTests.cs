using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Metrics;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class MetricSparklinesTests
{
    private static readonly DateTimeOffset T = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static MetricPoint Gauge(string name, double value)
        => MetricPoint.Number(ServiceName.From("svc"), name, "1", MetricKind.Gauge, T, value);

    [Fact]
    public void Accumulates_values_per_series_in_order()
    {
        var history = new MetricSparklines(capacity: 8);
        history.Add(Gauge("cpu", 1), 1);
        history.Add(Gauge("cpu", 2), 2);
        history.Add(Gauge("cpu", 3), 3);

        history.Values(Gauge("cpu", 99)).Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Drops_the_oldest_values_past_capacity()
    {
        var history = new MetricSparklines(capacity: 3);
        foreach (var v in new double[] { 1, 2, 3, 4, 5 })
        {
            history.Add(Gauge("cpu", v), v);
        }

        history.Values(Gauge("cpu", 0)).Should().Equal(3, 4, 5);
    }

    [Fact]
    public void Series_are_tracked_independently()
    {
        var history = new MetricSparklines(capacity: 8);
        history.Add(Gauge("cpu", 1), 1);
        history.Add(Gauge("mem", 9), 9);

        history.Values(Gauge("cpu", 0)).Should().Equal(1);
        history.Values(Gauge("mem", 0)).Should().Equal(9);
    }

    [Fact]
    public void Unknown_series_has_no_history()
        => new MetricSparklines(capacity: 8).Values(Gauge("nope", 0)).Should().BeEmpty();
}
