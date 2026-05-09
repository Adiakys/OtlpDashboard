namespace OpenTelemetryDashboard.Host.Configuration;

/// <summary>
/// CORS posture for the dashboard host. The default deployment serves the
/// SPA from <c>wwwroot/</c> on the same origin as the API, so no
/// cross-origin requests are involved and the empty default keeps the
/// surface minimal — no <c>Access-Control-Allow-Origin</c> header is
/// issued and browsers reject any cross-origin XHR before it reaches the
/// server.
/// <para>
/// Set <see cref="AllowedOrigins"/> when the SPA is served from a
/// different origin than the API (separate CDN, custom multi-domain
/// deployment, embedded panel, etc.). Each entry is an exact origin match
/// — no wildcards, no schemes mixing — and must include scheme and host
/// (e.g. <c>https://dashboard.example.com</c>).
/// </para>
/// </summary>
public sealed class DashboardCorsOptions
{
    public const string SectionName = "Dashboard:Cors";

    public string[] AllowedOrigins { get; init; } = [];
}
