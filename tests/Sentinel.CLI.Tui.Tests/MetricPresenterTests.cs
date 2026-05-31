using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Metrics;
using Sentinel.CLI.Tui.Views;

namespace Sentinel.CLI.Tui.Tests;

public class MetricPresenterTests
{
    private static readonly DateTimeOffset T = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static ServiceName Svc => ServiceName.From("svc");

    [Fact]
    public void FormatValue_gauge_shows_number()
        => MetricPresenter.FormatValue(MetricPoint.Number(Svc, "cpu", "1", MetricKind.Gauge, T, 0.5))
            .Should().Be("0.5");

    [Fact]
    public void FormatValue_histogram_shows_count_and_sum()
    {
        var metric = MetricPoint.Histogram(Svc, "lat", "ms", T, count: 10, sum: 100, min: 1, max: 50);

        MetricPresenter.FormatValue(metric).Should().Contain("count=10").And.Contain("sum=100");
    }

    [Fact]
    public void FormatLabels_renders_key_values()
    {
        var labels = TelemetryAttributes.From(new Dictionary<string, AttributeValue> { ["host"] = new AttributeValue.Text("a") });
        var metric = MetricPoint.Number(Svc, "cpu", "1", MetricKind.Gauge, T, 0.5, labels);

        MetricPresenter.FormatLabels(metric).Should().Be("host=a");
    }

    [Fact]
    public void SparkValue_uses_the_number_for_gauge_and_sum()
        => MetricPresenter.SparkValue(MetricPoint.Number(Svc, "cpu", "1", MetricKind.Gauge, T, 0.75))
            .Should().Be(0.75);

    [Fact]
    public void SparkValue_uses_the_mean_for_a_histogram()
    {
        var metric = MetricPoint.Histogram(Svc, "lat", "ms", T, count: 4, sum: 100, min: 1, max: 50);

        MetricPresenter.SparkValue(metric).Should().Be(25); // 100 / 4
    }
}
