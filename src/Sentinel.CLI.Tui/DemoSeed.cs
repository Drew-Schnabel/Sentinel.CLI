using Microsoft.Extensions.DependencyInjection;
using Sentinel.CLI.Application.Telemetry.Ports;
using Sentinel.CLI.Tui.Fixtures;

namespace Sentinel.CLI.Tui;

// Seeds the store with sample telemetry through the real write ports, so the UI
// renders fixture data via the same ITraceSink → store → ITraceQueries path the
// OTLP receiver will use. Invoked from Program.cs only when --demo is passed.
public static class DemoSeed
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var traceSink = services.GetRequiredService<ITraceSink>();
        var logSink = services.GetRequiredService<ILogSink>();

        foreach (var (trace, logs) in FixtureTraces.Build())
        {
            foreach (var span in trace.Spans)
            {
                await traceSink.AcceptAsync(span, cancellationToken);
            }
            foreach (var log in logs)
            {
                await logSink.AcceptAsync(log, cancellationToken);
            }
        }
    }
}
