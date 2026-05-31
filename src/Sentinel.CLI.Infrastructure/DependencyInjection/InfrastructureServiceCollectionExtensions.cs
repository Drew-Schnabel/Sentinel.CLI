using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sentinel.CLI.Application.Telemetry.Ports;
using Sentinel.CLI.Application.Telemetry.Queries;
using Sentinel.CLI.Infrastructure.Telemetry;

namespace Sentinel.CLI.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<StoreOptions>()
            .Bind(configuration.GetSection(StoreOptions.SectionName))
            .ValidateDataAnnotations();

        // Shared pause flag both stores honor and StoreControl flips (`:pause`/`:resume`).
        services.AddSingleton<IngestGate>();

        // One instance serves all four ports — writes and reads share its lock and state.
        services.AddSingleton<InMemoryTelemetryStore>();
        services.AddSingleton<ITraceSink>(sp => sp.GetRequiredService<InMemoryTelemetryStore>());
        services.AddSingleton<ILogSink>(sp => sp.GetRequiredService<InMemoryTelemetryStore>());
        services.AddSingleton<ITraceQueries>(sp => sp.GetRequiredService<InMemoryTelemetryStore>());
        services.AddSingleton<ILogQueries>(sp => sp.GetRequiredService<InMemoryTelemetryStore>());
        services.AddSingleton<ITelemetryStats>(sp => sp.GetRequiredService<InMemoryTelemetryStore>());

        // Metrics live in a sibling store (series-keyed, last-write-wins).
        services.AddSingleton<InMemoryMetricStore>();
        services.AddSingleton<IMetricSink>(sp => sp.GetRequiredService<InMemoryMetricStore>());
        services.AddSingleton<IMetricQueries>(sp => sp.GetRequiredService<InMemoryMetricStore>());

        // One control surface clears both stores (the TUI's `:clear`).
        services.AddSingleton<IStoreControl, StoreControl>();

        return services;
    }
}
