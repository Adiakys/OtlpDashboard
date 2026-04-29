namespace OpenTelemetryDashboard.Dashboards.Contracts;

/// <summary>
/// Wire shape for a dashboard. <see cref="Widgets"/> carries the typed grid
/// layout; <see cref="RowVersion"/> participates in optimistic concurrency on
/// save.
/// </summary>
public sealed record DashboardDto(
    Guid Id,
    string Name,
    IReadOnlyList<DashboardWidgetDto> Widgets,
    DateTimeOffset UpdatedAt,
    uint RowVersion);
