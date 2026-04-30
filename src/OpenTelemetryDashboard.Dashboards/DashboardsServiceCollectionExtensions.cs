using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Dashboards.Library;

namespace OpenTelemetryDashboard.Dashboards;

/// <summary>
/// DI registration for the Dashboards module. The storage adapter
/// (<c>IDashboardStore</c>, <c>IWidgetDefinitionStore</c>) is registered by
/// the Persistence module; this entry point owns options and the in-memory
/// widget library registry.
/// </summary>
public static class DashboardsServiceCollectionExtensions
{
    public static IServiceCollection AddDashboards(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<WidgetsOptions>()
            .Bind(configuration.GetSection(WidgetsOptions.SectionName));

        services.AddSingleton<IWidgetLibraryRegistry, FilesystemWidgetLibraryRegistry>();
        return services;
    }
}
