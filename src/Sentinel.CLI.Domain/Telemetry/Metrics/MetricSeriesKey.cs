using System.Globalization;
using Sentinel.CLI.Domain.Telemetry.Common;

namespace Sentinel.CLI.Domain.Telemetry.Metrics;

// Identity of a metric time series: service + metric name + the label set. Two points with the
// same key are the same series (last-write-wins in the store). Value equality via record struct.
public readonly record struct MetricSeriesKey
{
    public ServiceName Service { get; }
    public string Name { get; }
    public string LabelSignature { get; }

    private MetricSeriesKey(ServiceName service, string name, string labelSignature)
    {
        Service = service;
        Name = name;
        LabelSignature = labelSignature;
    }

    public static MetricSeriesKey For(MetricPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        var labels = point.Labels
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => string.Create(CultureInfo.InvariantCulture, $"{kv.Key}={kv.Value}"));
        return new MetricSeriesKey(point.Service, point.Name, string.Join(";", labels));
    }
}
