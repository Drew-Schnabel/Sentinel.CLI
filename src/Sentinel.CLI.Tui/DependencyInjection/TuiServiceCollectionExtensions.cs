using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Sentinel.CLI.Tui.DependencyInjection;

public static class TuiServiceCollectionExtensions
{
    public static IServiceCollection AddTui(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<TuiOptions>()
            .Bind(configuration.GetSection(TuiOptions.SectionName))
            .ValidateDataAnnotations();

        // ITraceQueries / ILogQueries are supplied by Infrastructure's store.
        // Fixtures are no longer the default source; pass --demo to seed the store
        // with sample telemetry through the real ingest path (see DemoSeed).
        services.AddSingleton<TuiRunner>();

        return services;
    }
}
