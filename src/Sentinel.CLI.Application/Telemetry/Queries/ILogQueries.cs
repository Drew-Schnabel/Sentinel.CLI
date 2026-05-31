using Sentinel.CLI.Domain.Telemetry.Common;
using Sentinel.CLI.Domain.Telemetry.Logs;

namespace Sentinel.CLI.Application.Telemetry.Queries;

public interface ILogQueries
{
    IAsyncEnumerable<LogRecord> StreamAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<LogRecord> ForTraceAsync(TraceId id, CancellationToken cancellationToken);
}
