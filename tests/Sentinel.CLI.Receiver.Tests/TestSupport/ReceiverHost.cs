using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sentinel.CLI.Application.DependencyInjection;
using Sentinel.CLI.Application.Telemetry.Ports;
using Sentinel.CLI.Infrastructure.DependencyInjection;

namespace Sentinel.CLI.Receiver.Tests.TestSupport;

// Starts the real receiver (AddOtlpReceiver + MapOtlpReceiver) on an ephemeral IPv4 loopback
// port so transport tests exercise actual Kestrel + gRPC/HTTP wiring. Never binds 4317/4318.
//
// Two flavors:
//   StartAsync           — a CapturingSink stands in for the store (assert on Sink).
//   StartWithRealStoreAsync — the real AddInfrastructure store is wired (assert via Services).
internal sealed class ReceiverHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly CapturingSink? _sink;

    public string BaseAddress { get; }

    public IServiceProvider Services => _app.Services;

    public CapturingSink Sink =>
        _sink ?? throw new InvalidOperationException("This host wires the real store, not a capturing sink.");

    private ReceiverHost(WebApplication app, string baseAddress, CapturingSink? sink)
    {
        _app = app;
        BaseAddress = baseAddress;
        _sink = sink;
    }

    public static Task<ReceiverHost> StartAsync(HttpProtocols protocols)
    {
        var sink = new CapturingSink();
        return StartAsync(protocols, sink, services =>
        {
            services.AddSingleton<ITraceSink>(sink);
            services.AddSingleton<ILogSink>(sink);
        });
    }

    public static Task<ReceiverHost> StartWithRealStoreAsync(HttpProtocols protocols)
        => StartAsync(protocols, sink: null, services =>
            services.AddApplication().AddInfrastructure(new ConfigurationBuilder().Build()));

    private static async Task<ReceiverHost> StartAsync(
        HttpProtocols protocols, CapturingSink? sink, Action<IServiceCollection> configureSinks)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        configureSinks(builder.Services);
        builder.Services.AddOtlpReceiver(new ConfigurationBuilder().Build());
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = protocols));

        var app = builder.Build();
        app.MapOtlpReceiver();
        await app.StartAsync();

        var address = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new ReceiverHost(app, address, sink);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
