using OpenTelemetryDashboard.Dashboards.Domain;

namespace OpenTelemetryDashboard.Dashboards.Storage;

/// <summary>
/// Read/write port for dashboard persistence. The Persistence assembly
/// supplies the EF Core-backed adapter; the Dashboards module stays free of
/// EF Core dependencies.
/// </summary>
public interface IDashboardStore
{
    /// <summary>
    /// Returns the singleton "default" dashboard, lazy-creating it on first
    /// access so callers never need to handle a missing-row case.
    /// </summary>
    Task<Dashboard> GetDefaultAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the default dashboard's name and layout. Throws
    /// <see cref="DashboardConcurrencyException"/> if the persisted
    /// <c>RowVersion</c> does not match <paramref name="expectedRowVersion"/>.
    /// </summary>
    Task<Dashboard> SaveDefaultAsync(
        string name,
        string layoutJson,
        uint expectedRowVersion,
        CancellationToken cancellationToken);
}
