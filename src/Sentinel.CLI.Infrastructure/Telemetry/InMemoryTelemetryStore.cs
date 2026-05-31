using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Sentinel.CLI.Application.Telemetry.Ports;
using Sentinel.CLI.Application.Telemetry.Queries;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Logs;
using Sentinel.CLI.Domain.Telemetry.Spans;

namespace Sentinel.CLI.Infrastructure.Telemetry;

// Single source of truth for received telemetry. Implements both write ports and
// both read queries because they share one lock and one set of backing collections.
//
// Concurrency contract: the receiver writes while the UI reads. The lock serializes
// all access, and no live object ever leaves a locked region — every read returns an
// immutable value or a freshly built snapshot, so a reader can never observe a writer
// mutating a collection mid-enumeration.
internal sealed class InMemoryTelemetryStore
    : ITraceSink, ILogSink, ITraceQueries, ILogQueries, ITelemetryStats
{
    private readonly Lock _gate = new();

    private readonly Dictionary<TraceId, Trace> _traces = [];
    private readonly Queue<TraceId> _insertionOrder = new();          // first-seen order, for FIFO eviction
    private readonly Dictionary<TraceId, List<LogRecord>> _logsByTrace = [];
    private readonly Queue<LogRecord> _logStream = new();             // capped recent-log ring

    private readonly StoreOptions _options;
    private readonly IngestGate _ingestGate;

    // Live trace ring-buffer cap, seeded from options. Mutable so `:capacity` can resize it at
    // runtime; guarded by _gate like the collections it bounds.
    private int _maxTraces;

    public InMemoryTelemetryStore(IOptions<StoreOptions> options, IngestGate ingestGate)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(ingestGate);
        _options = options.Value;
        _ingestGate = ingestGate;
        _maxTraces = _options.MaxTraces;
    }

    public int MaxTraces
    {
        get { lock (_gate) { return _maxTraces; } }
    }

    // Resize the trace ring buffer live. Shrinking evicts the oldest traces immediately.
    public void SetMaxTraces(int max)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(max, 1);
        lock (_gate)
        {
            _maxTraces = max;
            EvictTracesIfNeeded();
        }
    }

    public int TraceCount
    {
        get { lock (_gate) { return _traces.Count; } }
    }

    public int LogCount
    {
        get { lock (_gate) { return _logStream.Count; } }
    }

    // Drop every received trace and log. Used by the TUI's `:clear` to start a clean session.
    public void Clear()
    {
        lock (_gate)
        {
            _traces.Clear();
            _insertionOrder.Clear();
            _logsByTrace.Clear();
            _logStream.Clear();
        }
    }

    public ValueTask AcceptAsync(Span span, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(span);
        cancellationToken.ThrowIfCancellationRequested();

        if (_ingestGate.IsPaused)
        {
            return ValueTask.CompletedTask; // frozen — drop the span so the view holds still
        }

        lock (_gate)
        {
            if (!_traces.TryGetValue(span.TraceId, out var trace))
            {
                trace = Trace.Empty(span.TraceId);
                _traces[span.TraceId] = trace;
                _insertionOrder.Enqueue(span.TraceId);   // enqueue only on first sight
                EvictTracesIfNeeded();
            }
            trace.Record(span);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask AcceptAsync(LogRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        if (_ingestGate.IsPaused)
        {
            return ValueTask.CompletedTask; // frozen — drop the log
        }

        lock (_gate)
        {
            _logStream.Enqueue(record);
            while (_logStream.Count > _options.MaxLogStream)
            {
                _logStream.Dequeue();
            }

            if (record.TraceId is { } traceId)
            {
                if (!_logsByTrace.TryGetValue(traceId, out var list))
                {
                    list = [];
                    _logsByTrace[traceId] = list;
                }
                list.Add(record);
            }
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask<Trace?> FindAsync(TraceId id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            return _traces.TryGetValue(id, out var live)
                ? ValueTask.FromResult<Trace?>(Snapshot(live))
                : ValueTask.FromResult<Trace?>(null);
        }
    }

    public IAsyncEnumerable<Trace> RecentAsync(int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(limit);
        return Drain(SnapshotRecent(limit), cancellationToken);
    }

    public IAsyncEnumerable<LogRecord> StreamAsync(CancellationToken cancellationToken)
        => Drain(SnapshotLogStream(), cancellationToken);

    public IAsyncEnumerable<LogRecord> ForTraceAsync(TraceId id, CancellationToken cancellationToken)
        => Drain(SnapshotLogsForTrace(id), cancellationToken);

    // Caller MUST hold _gate.
    private void EvictTracesIfNeeded()
    {
        while (_traces.Count > _maxTraces && _insertionOrder.Count > 0)
        {
            var evicted = _insertionOrder.Dequeue();
            _traces.Remove(evicted);
            _logsByTrace.Remove(evicted);
        }
    }

    // Caller MUST hold _gate. Returns a Trace whose dictionary no writer can mutate.
    // Spans are immutable, so only the dictionary is copied — cheap at hundreds of spans.
    private static Trace Snapshot(Trace live)
    {
        var copy = Trace.Empty(live.Id);
        foreach (var span in live.Spans)
        {
            copy.Record(span);
        }
        return copy;
    }

    private List<Trace> SnapshotRecent(int limit)
    {
        lock (_gate)
        {
            var result = new List<Trace>(Math.Min(limit, _insertionOrder.Count));
            foreach (var id in _insertionOrder.Reverse())   // newest first
            {
                if (result.Count >= limit)
                {
                    break;
                }
                if (_traces.TryGetValue(id, out var live))
                {
                    result.Add(Snapshot(live));
                }
            }
            return result;
        }
    }

    private List<LogRecord> SnapshotLogStream()
    {
        lock (_gate)
        {
            return [.. _logStream];
        }
    }

    private List<LogRecord> SnapshotLogsForTrace(TraceId id)
    {
        lock (_gate)
        {
            return _logsByTrace.TryGetValue(id, out var list) ? [.. list] : [];
        }
    }

    // No lock here. The list was fully materialized under the lock; yielding it is safe.
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
