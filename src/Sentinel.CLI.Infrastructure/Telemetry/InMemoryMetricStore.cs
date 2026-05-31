using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Sentinel.CLI.Application.Telemetry.Ports;
using Sentinel.CLI.Application.Telemetry.Queries;
using Sentinel.CLI.Domain.Telemetry.Metrics;

namespace Sentinel.CLI.Infrastructure.Telemetry;

// Sibling to InMemoryTelemetryStore for metrics, which are series-keyed and last-write-wins
// (vs. the trace store's identity-keyed, whole-trace snapshots). Same concurrency contract:
// one lock, snapshot-on-read, FIFO eviction of the oldest series past the cap.
internal sealed class InMemoryMetricStore : IMetricSink, IMetricQueries
{
    private readonly Lock _gate = new();
    private readonly Dictionary<MetricSeriesKey, MetricPoint> _series = [];
    private readonly Queue<MetricSeriesKey> _insertionOrder = new();
    private readonly int _maxSeries;
    private readonly IngestGate _ingestGate;

    public InMemoryMetricStore(IOptions<StoreOptions> options, IngestGate ingestGate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ingestGate);
        _maxSeries = options.Value.MaxMetricSeries;
        _ingestGate = ingestGate;
    }

    public int SeriesCount
    {
        get { lock (_gate) { return _series.Count; } }
    }

    public ValueTask AcceptAsync(MetricPoint point, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(point);
        cancellationToken.ThrowIfCancellationRequested();

        if (_ingestGate.IsPaused)
        {
            return ValueTask.CompletedTask; // frozen — drop the metric point
        }

        var key = MetricSeriesKey.For(point);
        lock (_gate)
        {
            var isNewSeries = !_series.ContainsKey(key);
            _series[key] = point; // last-write-wins
            if (isNewSeries)
            {
                _insertionOrder.Enqueue(key);
                EvictIfNeeded();
            }
        }
        return ValueTask.CompletedTask;
    }

    // Drop every series. Paired with InMemoryTelemetryStore.Clear() via the StoreControl composite.
    public void Clear()
    {
        lock (_gate)
        {
            _series.Clear();
            _insertionOrder.Clear();
        }
    }

    public IAsyncEnumerable<MetricPoint> LatestAsync(CancellationToken cancellationToken)
        => Drain(SnapshotLatest(), cancellationToken);

    // Caller MUST hold _gate.
    private void EvictIfNeeded()
    {
        while (_series.Count > _maxSeries && _insertionOrder.Count > 0)
        {
            var evicted = _insertionOrder.Dequeue();
            _series.Remove(evicted);
        }
    }

    private List<MetricPoint> SnapshotLatest()
    {
        lock (_gate)
        {
            // Newest series first (MetricPoint is immutable; copying the list is enough).
            var result = new List<MetricPoint>(_series.Count);
            foreach (var key in _insertionOrder.Reverse())
            {
                if (_series.TryGetValue(key, out var point))
                {
                    result.Add(point);
                }
            }
            return result;
        }
    }

    private static async IAsyncEnumerable<T> Drain<T>(
        List<T> snapshot,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var item in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
        await Task.CompletedTask;
    }
}
