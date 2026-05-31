using FluentAssertions;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Metrics;

namespace Sentinel.CLI.Domain.Tests.Telemetry.Metrics;

public class MetricPointTests
{
    private static readonly DateTimeOffset T = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static ServiceName Svc => ServiceName.From("svc");

    private static TelemetryAttributes Labels(string key, string value)
        => TelemetryAttributes.From(new Dictionary<string, AttributeValue> { [key] = new AttributeValue.Text(value) });

    [Fact]
    public void Number_rejects_histogram_kind()
    {
        var act = () => MetricPoint.Number(Svc, "m", "1", MetricKind.Histogram, T, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Histogram_rejects_negative_count()
    {
        var act = () => MetricPoint.Histogram(Svc, "m", "ms", T, -1, null, null, null);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Number_defaults_labels_to_empty()
        => MetricPoint.Number(Svc, "m", "1", MetricKind.Gauge, T, 1).Labels.Count.Should().Be(0);

    [Fact]
    public void SeriesKey_same_service_name_and_labels_are_equal()
    {
        var a = MetricPoint.Number(Svc, "m", "1", MetricKind.Gauge, T, 1, Labels("k", "v"));
        var b = MetricPoint.Number(Svc, "m", "1", MetricKind.Gauge, T, 2, Labels("k", "v"));

        MetricSeriesKey.For(a).Should().Be(MetricSeriesKey.For(b));
    }

    [Fact]
    public void SeriesKey_different_labels_differ()
    {
        var a = MetricPoint.Number(Svc, "m", "1", MetricKind.Gauge, T, 1, Labels("k", "a"));
        var b = MetricPoint.Number(Svc, "m", "1", MetricKind.Gauge, T, 1, Labels("k", "b"));

        MetricSeriesKey.For(a).Should().NotBe(MetricSeriesKey.For(b));
    }
}
