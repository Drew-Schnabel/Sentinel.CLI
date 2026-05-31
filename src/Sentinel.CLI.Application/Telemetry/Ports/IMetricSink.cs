using Sentinel.CLI.Domain.Telemetry.Metrics;

namespace Sentinel.CLI.Application.Telemetry.Ports;

public interface IMetricSink
{
    ValueTask AcceptAsync(MetricPoint point, CancellationToken cancellationToken);
}
