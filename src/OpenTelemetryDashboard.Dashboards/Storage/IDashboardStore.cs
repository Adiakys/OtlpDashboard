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
    
    Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    
    Task AddAsync(Dashboard dashboard, CancellationToken cancellationToken = default);
    
    Task UpdateAsync(Dashboard dashboard, CancellationToken cancellationToken = default);
    
    Task DeleteAsync(Dashboard dashboard, CancellationToken cancellationToken = default);
}
