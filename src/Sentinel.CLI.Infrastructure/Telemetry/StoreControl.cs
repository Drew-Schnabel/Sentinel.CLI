using Sentinel.CLI.Application.Telemetry.Ports;

namespace Sentinel.CLI.Infrastructure.Telemetry;

// IStoreControl over both stores. Telemetry (traces/logs) and metrics live in separate stores
// with separate locks; a single Clear() fans out to both so the TUI clears everything at once
// without knowing the store topology.
internal sealed class StoreControl : IStoreControl
{
    private readonly InMemoryTelemetryStore _telemetry;
    private readonly InMemoryMetricStore _metrics;
    private readonly IngestGate _ingestGate;

    public StoreControl(InMemoryTelemetryStore telemetry, InMemoryMetricStore metrics, IngestGate ingestGate)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(ingestGate);
        _telemetry = telemetry;
        _metrics = metrics;
        _ingestGate = ingestGate;
    }

    public void Clear()
    {
        _telemetry.Clear();
        _metrics.Clear();
    }

    // One shared gate freezes both stores at once.
    public void SetPaused(bool paused)
    {
        if (paused)
        {
            _ingestGate.Pause();
        }
        else
        {
            _ingestGate.Resume();
        }
    }

    public bool IsPaused => _ingestGate.IsPaused;

    public int TraceCapacity => _telemetry.MaxTraces;

    public void SetTraceCapacity(int max) => _telemetry.SetMaxTraces(max);
}
