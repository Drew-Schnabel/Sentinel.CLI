using Sentinel.CLI.Domain.Telemetry.Spans;

namespace Sentinel.CLI.Application.Telemetry.Ports;

public interface ITraceSink
{
    ValueTask AcceptAsync(Span span, CancellationToken cancellationToken);
}
