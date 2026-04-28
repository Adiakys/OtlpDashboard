namespace OpenTelemetryDashboard.Api;

/// <summary>
/// Presentation metadata about the dashboard instance, exposed publicly via
/// <c>GET /api/v1/info</c> so the statically-compiled SPA can read it (the
/// SPA can't see env vars at runtime). Bound from the <c>Dashboard</c>
/// configuration section — same section as
/// <c>DashboardAuthOptions</c> but a different concern.
/// </summary>
public sealed class DashboardInfoOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>
    /// Display name shown in the sidebar header and the login form.
    /// Defaults to a neutral label so the UI never shows an empty string.
    /// </summary>
    public string ApplicationName { get; set; } = "OTel Dashboard";
}
