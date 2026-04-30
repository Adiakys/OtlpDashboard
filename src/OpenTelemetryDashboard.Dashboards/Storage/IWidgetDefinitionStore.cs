using OpenTelemetryDashboard.Dashboards.Domain;

namespace OpenTelemetryDashboard.Dashboards.Storage;

/// <summary>
/// Read/write port for custom widget definitions. The Persistence assembly
/// supplies the EF Core-backed adapter; the Dashboards module stays free of
/// EF Core dependencies. Library-sourced widgets do not flow through this
/// store — they are resolved at render time from the filesystem registry.
/// </summary>
public interface IWidgetDefinitionStore
{
    Task<IReadOnlyList<WidgetDefinition>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<WidgetDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(WidgetDefinition definition, CancellationToken cancellationToken = default);

    Task UpdateAsync(WidgetDefinition definition, CancellationToken cancellationToken = default);

    Task DeleteAsync(WidgetDefinition definition, CancellationToken cancellationToken = default);
}
