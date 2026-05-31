namespace Sentinel.CLI.Application.Telemetry.Queries;

// Cheap retained-telemetry counts for the status bar.
public interface ITelemetryStats
{
    int TraceCount { get; }
    int LogCount { get; }
}
