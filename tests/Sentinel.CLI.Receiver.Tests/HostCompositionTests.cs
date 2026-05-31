using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sentinel.CLI.Application.DependencyInjection;
using Sentinel.CLI.Application.Telemetry.Ports;
using Sentinel.CLI.Application.Telemetry.Queries;
using Sentinel.CLI.Infrastructure.DependencyInjection;
using Sentinel.CLI.Receiver.Telemetry;
using Sentinel.CLI.Receiver.Tests.TestSupport;
using Sentinel.CLI.Tui;
using Sentinel.CLI.Tui.DependencyInjection;

namespace Sentinel.CLI.Receiver.Tests;

// Covers the one piece the transport tests don't: Program.cs's real composition and
// failure path, where "green but broken" hides because every other test builds its own host.
public class HostCompositionTests
{
    [Fact]
    public void Real_composition_resolves_shared_store_and_receiver_services()
    {
        var config = new ConfigurationBuilder().Build();
        using var sp = new ServiceCollection()
            .AddApplication()
            .AddInfrastructure(config)
            .AddOtlpReceiver(config)
            .AddTui(config)
            .BuildServiceProvider();

        // One store instance backs all four ports (the silent-DI-break guard).
        var sink = sp.GetRequiredService<ITraceSink>();
        sp.GetRequiredService<ILogSink>().Should().BeSameAs(sink);
        sp.GetRequiredService<ITraceQueries>().Should().BeSameAs(sink);
        sp.GetRequiredService<ILogQueries>().Should().BeSameAs(sink);

        sp.GetRequiredService<ReceiverDiagnostics>().Should().NotBeNull();
        sp.GetRequiredService<TuiRunner>().Should().NotBeNull();

        // gRPC services are activated from the provider per call — prove their deps resolve.
        ActivatorUtilities.CreateInstance<TraceExportService>(sp).Should().NotBeNull();
        ActivatorUtilities.CreateInstance<LogsExportService>(sp).Should().NotBeNull();
    }

    [Fact]
    public async Task Binding_an_already_used_port_throws_address_in_use()
    {
        await using var first = await ReceiverHost.StartAsync(HttpProtocols.Http1);
        var port = new Uri(first.BaseAddress).Port;

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, port, listen => listen.Protocols = HttpProtocols.Http1));
        await using var second = builder.Build();

        var start = async () => await second.StartAsync();

        // Kestrel surfaces a port clash as IOException → AddressInUseException → SocketException.
        // Program.cs walks this chain, so assert the chain contains AddressInUseException — the
        // shape its catch filter must recognize (a naive "IOException with SocketException inner"
        // filter would miss it, since the direct inner is AddressInUseException).
        var thrown = (await start.Should().ThrowAsync<IOException>()).Which;

        var chain = new List<Exception>();
        for (Exception? e = thrown; e is not null; e = e.InnerException)
        {
            chain.Add(e);
        }
        chain.Should().Contain(e => e is AddressInUseException);
    }
}
