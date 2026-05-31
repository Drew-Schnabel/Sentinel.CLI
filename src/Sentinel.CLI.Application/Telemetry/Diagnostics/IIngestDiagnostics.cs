namespace Sentinel.CLI.Application.Telemetry.Diagnostics;

// Read-only view of receiver drop counters, exposed as an Application port so the TUI can
// surface them without referencing the receiver implementation.
public interface IIngestDiagnostics
{
    long DroppedSpans { get; }
    long DroppedLogs { get; }
    long DroppedMetrics { get; }
}
