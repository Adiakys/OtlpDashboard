namespace OpenTelemetryDashboard.Api.Contracts;

/// <summary>
/// Public metadata response body for <c>GET /api/v1/info</c>.
/// <para>
/// All "infra-shape" fields (<see cref="Version"/>,
/// <see cref="StorageProvider"/>, <see cref="TelemetryLimits"/>) are
/// gated behind authentication: anonymous visitors on the login screen
/// see <c>null</c> for them so we don't leak build/deployment details
/// to the public internet. <see cref="ApplicationName"/> is the only
/// always-available field — the login form needs it to render.
/// </para>
/// <para>
/// The Host registers a fully-populated instance as a singleton at boot
/// time. The endpoint resolves it from DI and returns either the full
/// record (authenticated) or a redacted copy (anonymous) — see
/// <c>InfoEndpoints.GetInfo</c>. Adding a new field is therefore a
/// two-step change: declare it here, populate it in the Host's wiring.
/// </para>
/// </summary>
public sealed class DashboardInfoDto(string applicationName)
{
    public string ApplicationName { get; init; } = applicationName;

    public string? Version { get; init; }

    public string? StorageProvider { get; init; }

    public TelemetryLimitsDto? TelemetryLimits { get; init; }
    
    public int? QueryMaxWindowHours { get; init; }
}

public sealed record TelemetryLimitsDto(
    double MaxLogDays,
    double MaxTraceDays,
    double MaxMetricDays,
    int SweepIntervalMinutes);