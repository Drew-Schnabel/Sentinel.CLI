using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.CLI.Application.Telemetry.Ports;
using Sentinel.CLI.Application.Telemetry.Queries;
using Sentinel.CLI.Infrastructure.DependencyInjection;
using static Sentinel.CLI.Infrastructure.Tests.TestHelpers.StoreTestHelpers;

namespace Sentinel.CLI.Infrastructure.Tests.DependencyInjection;

public class InfrastructureServiceCollectionExtensionsTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private static ServiceProvider BuildProvider() =>
        new ServiceCollection()
            .AddInfrastructure(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

    [Fact]
    public void AddInfrastructure_resolves_one_store_shared_across_all_four_ports()
    {
        using var sp = BuildProvider();

        var sink = sp.GetRequiredService<ITraceSink>();

        // The shared-instance contract: writes via the sinks must be visible to the
        // queries. Four separate instances would still pass every isolated store test
        // while showing an empty screen at runtime — this is the guard against that.
        sp.GetRequiredService<ILogSink>().Should().BeSameAs(sink);
        sp.GetRequiredService<ITraceQueries>().Should().BeSameAs(sink);
        sp.GetRequiredService<ILogQueries>().Should().BeSameAs(sink);
    }

    [Fact]
    public async Task AddInfrastructure_write_via_sink_is_readable_via_queries()
    {
        using var sp = BuildProvider();
        var span = MakeSpan(TraceId(1), SpanId(1));

        await sp.GetRequiredService<ITraceSink>().AcceptAsync(span, None);
        var trace = await sp.GetRequiredService<ITraceQueries>().FindAsync(TraceId(1), None);

        trace.Should().NotBeNull();
    }
}
