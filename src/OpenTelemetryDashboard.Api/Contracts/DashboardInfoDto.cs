namespace OpenTelemetryDashboard.Api.Contracts;

/// <summary>
/// Public metadata response body for <c>GET /api/v1/info</c>.
/// <see cref="Version"/> reflects <c>AssemblyInformationalVersion</c>
/// baked in at build time (see <c>Directory.Build.props</c>) and is only
/// returned to authenticated callers — unauthenticated visitors on the
/// login screen see <c>null</c> so we don't leak the build version to
/// anyone on the internet.
/// </summary>
public sealed record DashboardInfoDto(string ApplicationName, string? Version);
