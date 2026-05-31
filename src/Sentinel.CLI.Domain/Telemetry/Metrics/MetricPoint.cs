using Sentinel.CLI.Domain.Telemetry.Common;

namespace Sentinel.CLI.Domain.Telemetry.Metrics;

// A single observed metric data point. Gauge/Sum carry a numeric Value; Histogram carries
// Count/Sum/Min/Max (buckets are not modeled in v0). Immutable, like Span/LogRecord.
public sealed class MetricPoint
{
    public ServiceName Service { get; }
    public string Name { get; }
    public string Unit { get; }
    public MetricKind Kind { get; }
    public DateTimeOffset Timestamp { get; }
    public TelemetryAttributes Labels { get; }

    public double? Value { get; }   // Gauge / Sum
    public long? Count { get; }     // Histogram
    public double? Sum { get; }     // Histogram
    public double? Min { get; }     // Histogram
    public double? Max { get; }     // Histogram

    private MetricPoint(
        ServiceName service, string name, string unit, MetricKind kind,
        DateTimeOffset timestamp, TelemetryAttributes labels,
        double? value, long? count, double? sum, double? min, double? max)
    {
        Service = service;
        Name = name;
        Unit = unit;
        Kind = kind;
        Timestamp = timestamp;
        Labels = labels;
        Value = value;
        Count = count;
        Sum = sum;
        Min = min;
        Max = max;
    }

    public static MetricPoint Number(
        ServiceName service, string name, string unit, MetricKind kind,
        DateTimeOffset timestamp, double value, TelemetryAttributes? labels = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (kind is not (MetricKind.Gauge or MetricKind.Sum))
        {
            throw new ArgumentException("Number metric must be Gauge or Sum.", nameof(kind));
        }
        return new MetricPoint(
            service, name, unit ?? string.Empty, kind, timestamp,
            labels ?? TelemetryAttributes.Empty, value, null, null, null, null);
    }

    public static MetricPoint Histogram(
        ServiceName service, string name, string unit, DateTimeOffset timestamp,
        long count, double? sum, double? min, double? max, TelemetryAttributes? labels = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return new MetricPoint(
            service, name, unit ?? string.Empty, MetricKind.Histogram, timestamp,
            labels ?? TelemetryAttributes.Empty, null, count, sum, min, max);
    }
}
