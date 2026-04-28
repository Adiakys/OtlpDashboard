namespace OpenTelemetryDashboard.Dashboards.Contracts;

/// <summary>
/// Save payload for the default dashboard. <c>RowVersion</c> must match the
/// last value returned by <c>GET</c>; otherwise the server responds 409.
/// </summary>
public sealed record SaveDashboardRequest(
    string Name,
    string LayoutJson,
    uint RowVersion);
