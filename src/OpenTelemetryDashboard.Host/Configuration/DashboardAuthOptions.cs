namespace OpenTelemetryDashboard.Host.Configuration;

/// <summary>
/// Static-token authentication config. Bound from the <c>Dashboard</c> section
/// (environment variables: <c>DASHBOARD__BROWSERTOKEN</c>,
/// <c>DASHBOARD__OTLP__APIKEY</c>, <c>DASHBOARD__AUTH__ALLOWANONYMOUS</c>).
/// <para>
/// In Development, an empty token degrades to allow-all so local dev and the
/// existing integration tests keep running without changes. In Production,
/// missing tokens fail-closed: the host refuses to start unless the operator
/// has explicitly opted in via <see cref="Auth"/>.<see cref="AuthPostureOptions.AllowAnonymous"/>.
/// </para>
/// </summary>
public sealed class DashboardAuthOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>Bearer token accepted by the read-side Query API.</summary>
    public string? BrowserToken { get; set; }

    public OtlpAuthOptions Otlp { get; set; } = new();

    public AuthPostureOptions Auth { get; set; } = new();
}

public sealed class OtlpAuthOptions
{
    /// <summary>Bearer token accepted by the OTLP ingestion endpoints (HTTP + gRPC).</summary>
    public string? ApiKey { get; set; }
}

public sealed class AuthPostureOptions
{
    /// <summary>
    /// Explicit opt-in to public, unauthenticated access. Default is
    /// <c>false</c>: in Production, missing tokens fail the boot with a
    /// clear error. Set to <c>true</c> only for air-gapped / private-network
    /// deployments where every API and the OTLP ingest endpoint can safely
    /// be public.
    /// </summary>
    public bool AllowAnonymous { get; set; }
}
