using Sentinel.CLI.Domain.Telemetry.Metrics;

namespace Sentinel.CLI.Application.Telemetry.Queries;

public interface IMetricQueries
{
    // Latest data point per series, newest series first.
    IAsyncEnumerable<MetricPoint> LatestAsync(CancellationToken cancellationToken);

    // Number of distinct series retained (cheap; for the status bar).
    int SeriesCount { get; }
}
