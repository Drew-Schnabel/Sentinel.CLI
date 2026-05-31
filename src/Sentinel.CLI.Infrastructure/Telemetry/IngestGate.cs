namespace Sentinel.CLI.Infrastructure.Telemetry;

// Single shared pause flag for ingest. Both stores check it at the top of their AcceptAsync paths
// and drop incoming telemetry while paused; StoreControl flips it for `:pause`/`:resume`. A plain
// volatile bool is enough: writes come from the UI thread, reads from receiver threads, and a torn
// read is impossible for a bool — no lock needed.
internal sealed class IngestGate
{
    private volatile bool _paused;

    public bool IsPaused => _paused;

    public void Pause() => _paused = true;

    public void Resume() => _paused = false;
}
