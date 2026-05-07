using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Dashboards.Library;
using OpenTelemetryDashboard.Dashboards.Seeding;

namespace OpenTelemetryDashboard.Dashboards;

/// <summary>
/// DI registration for the Dashboards module. The storage adapter
/// (<c>IDashboardStore</c>, <c>IWidgetDefinitionStore</c>) is registered by
/// the Persistence module; this entry point owns options, the in-memory
/// pack registry, and the install pipeline.
/// </summary>
public static class DashboardsServiceCollectionExtensions
{
    public static IServiceCollection AddDashboards(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<PacksOptions>()
            .Bind(configuration.GetSection(PacksOptions.SectionName));

        // The pack registry is registered both under its concrete type
        // (so the installer can read the primary path) and the read-side
        // port that every other consumer talks to. Two registrations
        // point at the same singleton so cache invalidation lines up.
        services.AddSingleton<FilesystemPackRegistry>();
        services.AddSingleton<IPackRegistry>(sp =>
            sp.GetRequiredService<FilesystemPackRegistry>());

        // Picker contract: a flat library list synthesized from the
        // pack registry. Singleton so the adapter doesn't allocate on
        // every picker open — its state is just the IPackRegistry it
        // delegates to.
        services.AddSingleton<IWidgetLibraryRegistry, WidgetLibraryRegistryAdapter>();

        services.AddSingleton<IGitInstaller, LibGit2SharpInstaller>();
        services.AddSingleton<IPackInstaller, PackInstallService>();

        services.AddSingleton<IBuiltinDashboardSeeder, BuiltinDashboardSeeder>();

        return services;
    }
}
