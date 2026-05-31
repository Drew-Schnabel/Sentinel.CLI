using Sentinel.CLI.Application.Telemetry.Diagnostics;

namespace Sentinel.CLI.Receiver.Telemetry;

// Process-wide receiver counters. Thread-safe; written from Kestrel handler threads,
// read by the UI via the IIngestDiagnostics port.
public sealed class ReceiverDiagnostics : IIngestDiagnostics
{
    private long _droppedSpans;
    private long _droppedLogs;
    private long _droppedMetrics;

    public long DroppedSpans => Interlocked.Read(ref _droppedSpans);
    public long DroppedLogs => Interlocked.Read(ref _droppedLogs);
    public long DroppedMetrics => Interlocked.Read(ref _droppedMetrics);

    public void AddDroppedSpans(int count) => Interlocked.Add(ref _droppedSpans, count);
    public void AddDroppedLogs(int count) => Interlocked.Add(ref _droppedLogs, count);
    public void AddDroppedMetrics(int count) => Interlocked.Add(ref _droppedMetrics, count);
}
