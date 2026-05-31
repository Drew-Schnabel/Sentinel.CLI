using System.ComponentModel.DataAnnotations;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sentinel.CLI;
using Sentinel.CLI.Application.DependencyInjection;
using Sentinel.CLI.Infrastructure.DependencyInjection;
using Sentinel.CLI.Logging;
using Sentinel.CLI.Receiver;
using Sentinel.CLI.Tui;
using Sentinel.CLI.Tui.DependencyInjection;

// --server runs the receiver + store headless (no TUI) — the basis for a future multi-window
// client/server split where separate viewer processes query this one. See ADR-0006.
var serverMode = HostArgs.Has(args, "--server");

// The TUI needs a real terminal. If invoked in a pipe / non-tty (CI, redirected output),
// refuse with a clear message rather than crashing inside the Terminal.Gui driver — and
// before binding any ports. (Skipped in --server mode, which has no TUI.)
if (!serverMode && !TerminalGuard.IsInteractive(Console.IsOutputRedirected, Console.IsInputRedirected))
{
    await Console.Error.WriteLineAsync(
        "Sentinel needs an interactive terminal, but stdin/stdout looks redirected (piped or non-tty). " +
        "Run `sentinel` directly in a terminal.");
    return 2;
}

// Strip the bare mode flags before the command-line config provider sees them (see HostArgs),
// so `--Receiver:GrpcPort=…` / `--Receiver:HttpPort=…` bind regardless of argument order.
var builder = WebApplication.CreateSlimBuilder(HostArgs.WithoutModeFlags(args));

// Terminal.Gui owns the screen. Silence the framework's console logging (Kestrel / hosting
// "Now listening on…", request logs, etc.) so it can't paint over and corrupt the TUI — then
// route warnings/errors to a file so they stay diagnosable instead of vanishing.
builder.Logging.ClearProviders();
var logPath = FileLogging.Configure(builder.Logging, builder.Configuration);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddOtlpReceiver(builder.Configuration)
    .AddTui(builder.Configuration);

// Bind the ports through ReceiverOptions and enforce its [Range] validation here, so an
// out-of-range Receiver:GrpcPort/HttpPort fails with a clear message rather than throwing
// raw inside Kestrel's bind.
var receiverOptions = builder.Configuration.GetSection(ReceiverOptions.SectionName).Get<ReceiverOptions>()
    ?? new ReceiverOptions();
Validator.ValidateObject(receiverOptions, new ValidationContext(receiverOptions), validateAllProperties: true);

builder.WebHost.ConfigureKestrel(kestrel =>
{
    // Local-first: bind loopback only, never all interfaces.
    kestrel.ListenLocalhost(receiverOptions.GrpcPort, listen => listen.Protocols = HttpProtocols.Http2); // OTLP/gRPC (h2c)
    kestrel.ListenLocalhost(receiverOptions.HttpPort, listen => listen.Protocols = HttpProtocols.Http1); // OTLP/HTTP
});

var app = builder.Build();
app.MapOtlpReceiver();

try
{
    await app.StartAsync(); // Kestrel binds here; a bind failure surfaces before the TUI launches.
}
catch (Exception ex) when (IsAddressInUse(ex))
{
    await Console.Error.WriteLineAsync(
        $"Sentinel could not bind ports {receiverOptions.GrpcPort}/{receiverOptions.HttpPort} — is another " +
        "OTLP receiver (or another Sentinel) already running? Set Receiver:GrpcPort / Receiver:HttpPort to relocate.");
    return 1;
}

if (serverMode)
{
    Console.WriteLine(
        $"Sentinel receiver running headless on 127.0.0.1:{receiverOptions.GrpcPort} (gRPC) / " +
        $":{receiverOptions.HttpPort} (HTTP). Press Ctrl-C to stop.");
    Console.WriteLine(logPath is null ? "  (file logging unavailable)" : $"  logs: {logPath}");

    var shutdown = new TaskCompletionSource();
    using var onInterrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; shutdown.TrySetResult(); });
    using var onTerminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; shutdown.TrySetResult(); });

    await shutdown.Task;
    await app.StopAsync();
    return 0;
}

var tui = app.Services.GetRequiredService<TuiRunner>();

// SIGTERM (kill / docker stop / CI teardown) is invisible to the TUI keyboard loop;
// translate it into a normal stop so StopAsync drains Kestrel and the terminal is restored.
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
{
    context.Cancel = true;
    tui.RequestStop();
});

try
{
    if (HostArgs.Has(args, "--demo"))
    {
        await DemoSeed.SeedAsync(app.Services);
    }

    tui.Run(); // blocks the main thread until the user quits
}
finally
{
    await app.StopAsync();
}

return 0;

// A port clash surfaces from Kestrel as AddressInUseException, sometimes wrapped in an
// IOException, sometimes over a SocketException — walk the chain rather than guess the shape.
static bool IsAddressInUse(Exception? error)
{
    for (var current = error; current is not null; current = current.InnerException)
    {
        if (current is AddressInUseException
            or SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
        {
            return true;
        }
    }
    return false;
}
