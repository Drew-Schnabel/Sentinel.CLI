using Sentinel.CLI.Domain.Telemetry.Metrics;

namespace Sentinel.CLI.Tui.Views;

// Keeps a bounded rolling history of recent values per metric series, so the metrics view can
// draw a trend. The store itself is last-write-wins (one point per series), so history is
// accumulated here by sampling on each refresh tick. Capacity bounds both the sparkline width
// and memory per series.
internal sealed class MetricSparklines
{
    private readonly int _capacity;
    private readonly Dictionary<MetricSeriesKey, Queue<double>> _series = [];

    public MetricSparklines(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    public void Add(MetricPoint point, double value)
    {
        ArgumentNullException.ThrowIfNull(point);
        var key = MetricSeriesKey.For(point);
        if (!_series.TryGetValue(key, out var window))
        {
            window = new Queue<double>(_capacity);
            _series[key] = window;
        }
        window.Enqueue(value);
        while (window.Count > _capacity)
        {
            window.Dequeue();
        }
    }

    public IReadOnlyList<double> Values(MetricPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        return _series.TryGetValue(MetricSeriesKey.For(point), out var window) ? [.. window] : [];
    }
}
