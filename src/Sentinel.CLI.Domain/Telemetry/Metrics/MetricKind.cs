namespace Sentinel.CLI.Domain.Telemetry.Metrics;

public enum MetricKind
{
    Unspecified = 0,
    Gauge = 1,
    Sum = 2,
    Histogram = 3,
}
