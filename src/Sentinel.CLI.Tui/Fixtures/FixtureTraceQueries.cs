using Sentinel.CLI.Application.Telemetry.Queries;
using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Logs;
using Sentinel.CLI.Domain.Telemetry.Spans;

namespace Sentinel.CLI.Tui.Fixtures;

internal sealed class FixtureTraceQueries : ITraceQueries, ILogQueries
{
    private readonly Dictionary<TraceId, Trace> _traces;
    private readonly Dictionary<TraceId, IReadOnlyList<LogRecord>> _logsByTrace;
    private readonly List<Trace> _ordered;
    private readonly List<LogRecord> _allLogs;

    public FixtureTraceQueries()
    {
        var fixtures = FixtureTraces.Build();
        _traces = fixtures.ToDictionary(x => x.Trace.Id, x => x.Trace);
        _logsByTrace = fixtures.ToDictionary(x => x.Trace.Id, x => x.Logs);
        _ordered = fixtures.Select(x => x.Trace).ToList();
        _allLogs = fixtures.SelectMany(x => x.Logs)
            .OrderBy(l => l.Timestamp).ToList();
    }

    public ValueTask<Trace?> FindAsync(TraceId id, CancellationToken cancellationToken)
        => ValueTask.FromResult(_traces.TryGetValue(id, out var t) ? t : null);

    public async IAsyncEnumerable<Trace> RecentAsync(
        int limit,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        foreach (var trace in _ordered.Take(limit))
        {
            yield return trace;
        }
    }

    public async IAsyncEnumerable<LogRecord> StreamAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        foreach (var log in _allLogs)
        {
            yield return log;
        }
    }

    public async IAsyncEnumerable<LogRecord> ForTraceAsync(
        TraceId id,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        if (!_logsByTrace.TryGetValue(id, out var logs))
        {
            yield break;
        }
        foreach (var log in logs)
        {
            yield return log;
        }
    }
}
