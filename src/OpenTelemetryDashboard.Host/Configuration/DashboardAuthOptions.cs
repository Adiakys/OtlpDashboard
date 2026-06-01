namespace OpenTelemetryDashboard.Host.Configuration;

/// <summary>
/// Static-token authentication config. Bound from the <c>Dashboard</c> section
/// (environment variables: <c>DASHBOARD__BROWSERTOKEN</c>,
/// <c>DASHBOARD__OTLP__APIKEY</c>). Auth is opt-in per surface: an empty token
/// leaves that surface public, identically in Development and Production.
/// </summary>
public sealed class DashboardAuthOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>Bearer token accepted by the read-side Query API + SPA. Empty
    /// leaves the read API and the SPA public (no login required).</summary>
    public string? BrowserToken { get; set; }

    public OtlpAuthOptions Otlp { get; set; } = new();
}

public sealed class OtlpAuthOptions
{
    /// <summary>Bearer token accepted by the OTLP ingestion endpoints (HTTP + gRPC).
    /// Empty leaves ingestion public.</summary>
    public string? ApiKey { get; set; }
}
