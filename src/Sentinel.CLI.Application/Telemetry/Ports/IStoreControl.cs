namespace Sentinel.CLI.Application.Telemetry.Ports;

// Control surface for the in-memory stores, separate from the write ports (sinks) and read
// queries. Lets the TUI drop everything received so far (`:clear`) and freeze/unfreeze ingest
// (`:pause`/`:resume`) without taking a dependency on Infrastructure. A single call spans every
// store so the UI doesn't have to know there are two (traces/logs vs. metrics).
public interface IStoreControl
{
    // Drop everything received so far.
    void Clear();

    // Freeze (true) or unfreeze (false) ingest. While paused, incoming telemetry is dropped so the
    // view holds still. (One method rather than Pause/Resume — "Resume" is a reserved keyword in
    // some .NET languages, which the analyzers flag on a public interface member.)
    void SetPaused(bool paused);

    // Whether ingest is currently frozen.
    bool IsPaused { get; }

    // The current trace ring-buffer capacity, and a live resize (shrinking evicts oldest now).
    int TraceCapacity { get; }
    void SetTraceCapacity(int max);
}
