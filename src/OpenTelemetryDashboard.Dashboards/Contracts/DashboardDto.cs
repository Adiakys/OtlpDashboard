namespace OpenTelemetryDashboard.Dashboards.Contracts;

/// <summary>
/// Wire shape for a dashboard. <c>LayoutJson</c> is a verbatim JSON document
/// owned by the client; <c>RowVersion</c> participates in optimistic
/// concurrency on save.
/// </summary>
public sealed record DashboardDto(
    Guid Id,
    string Name,
    string LayoutJson,
    DateTimeOffset UpdatedAt,
    uint RowVersion);
