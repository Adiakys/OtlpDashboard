using OpenTelemetryDashboard.Dashboards.Domain;

namespace OpenTelemetryDashboard.Dashboards.Storage;

/// <summary>
/// Read/write port for dashboard persistence. The Persistence assembly
/// supplies the EF Core-backed adapter; the Dashboards module stays free of
/// EF Core dependencies.
/// </summary>
public interface IDashboardStore
{
    Task<IReadOnlyList<Dashboard>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight projection of every persisted dashboard's id. Used by the
    /// built-in seeder to decide which library files are already present in
    /// the store without paying for the widget hydration that
    /// <see cref="GetAllAsync"/> performs.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetAllIdsAsync(CancellationToken cancellationToken = default);

    Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    
    Task AddAsync(Dashboard dashboard, CancellationToken cancellationToken = default);
    
    Task UpdateAsync(Dashboard dashboard, CancellationToken cancellationToken = default);
    
    Task DeleteAsync(Dashboard dashboard, CancellationToken cancellationToken = default);
}
