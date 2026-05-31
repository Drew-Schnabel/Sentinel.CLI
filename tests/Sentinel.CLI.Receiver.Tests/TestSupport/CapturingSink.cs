using Sentinel.CLI.Application.Telemetry.Ports;
using Sentinel.CLI.Domain.Telemetry.Logs;
using Sentinel.CLI.Domain.Telemetry.Spans;

namespace Sentinel.CLI.Receiver.Tests.TestSupport;

// Stand-in for the store: records what the receiver pushes so tests can assert on it.
internal sealed class CapturingSink : ITraceSink, ILogSink
{
    private readonly Lock _gate = new();
    private readonly List<Span> _spans = [];
    private readonly List<LogRecord> _logs = [];

    public IReadOnlyList<Span> Spans
    {
        get { lock (_gate) { return [.. _spans]; } }
    }

    public IReadOnlyList<LogRecord> Logs
    {
        get { lock (_gate) { return [.. _logs]; } }
    }

    public ValueTask AcceptAsync(Span span, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _spans.Add(span);
        }
        return ValueTask.CompletedTask;
    }

    public ValueTask AcceptAsync(LogRecord record, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _logs.Add(record);
        }
        return ValueTask.CompletedTask;
    }
}
