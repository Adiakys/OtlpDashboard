namespace OpenTelemetryDashboard.Host.Configuration;

/// <summary>
/// Static-token authentication config. Bound from the <c>Dashboard</c> section
/// (environment variables: <c>DASHBOARD__BROWSERTOKEN</c>,
/// <c>DASHBOARD__OTLP__APIKEY</c>).
/// <para>
/// If a token is empty/unset the corresponding authorization policy degrades
/// to allow-all — the endpoints remain public. This keeps dev and existing
/// integration tests running without changes; production deployments opt in
/// by setting both variables explicitly.
/// </para>
/// </summary>
public sealed class DashboardAuthOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>Bearer token accepted by the read-side Query API.</summary>
    public string? BrowserToken { get; set; }

    public OtlpAuthOptions Otlp { get; set; } = new();
}

public sealed class OtlpAuthOptions
{
    /// <summary>Bearer token accepted by the OTLP ingestion endpoints (HTTP + gRPC).</summary>
    public string? ApiKey { get; set; }
}
