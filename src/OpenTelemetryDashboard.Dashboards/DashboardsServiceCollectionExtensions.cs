using Microsoft.Extensions.DependencyInjection;

namespace OpenTelemetryDashboard.Dashboards;

/// <summary>
/// DI registration for the Dashboards module. Currently a no-op marker —
/// the storage adapter (<c>IDashboardStore</c>) is registered by the
/// Persistence module's <c>AddTelemetryPersistenceCore</c>. Kept as a
/// stable extension point so options/services added later land here.
/// </summary>
public static class DashboardsServiceCollectionExtensions
{
    public static IServiceCollection AddDashboards(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
