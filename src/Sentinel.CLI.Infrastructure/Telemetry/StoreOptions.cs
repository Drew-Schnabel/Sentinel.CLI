using System.ComponentModel.DataAnnotations;

namespace Sentinel.CLI.Infrastructure.Telemetry;

public sealed class StoreOptions
{
    public const string SectionName = "Store";

    // Ring-buffer cap on retained traces. Oldest first-seen trace is evicted past this.
    [Range(1, 100_000)]
    public int MaxTraces { get; set; } = 500;

    // Ring-buffer cap on the global recent-log stream, independent of MaxTraces.
    [Range(1, 1_000_000)]
    public int MaxLogStream { get; set; } = 5_000;

    // Cap on distinct metric series retained (oldest first-seen series evicted past this).
    [Range(1, 1_000_000)]
    public int MaxMetricSeries { get; set; } = 2_000;
}
