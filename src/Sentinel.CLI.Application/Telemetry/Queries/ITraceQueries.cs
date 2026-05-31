using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Spans;

namespace Sentinel.CLI.Application.Telemetry.Queries;

public interface ITraceQueries
{
    ValueTask<Trace?> FindAsync(TraceId id, CancellationToken cancellationToken);

    IAsyncEnumerable<Trace> RecentAsync(int limit, CancellationToken cancellationToken);
}
