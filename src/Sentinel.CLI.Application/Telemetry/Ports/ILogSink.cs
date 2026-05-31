using Sentinel.CLI.Domain.Telemetry.Logs;

namespace Sentinel.CLI.Application.Telemetry.Ports;

public interface ILogSink
{
    ValueTask AcceptAsync(LogRecord record, CancellationToken cancellationToken);
}
