namespace OpenTelemetryDashboard.Host.Hosting;

/// <summary>
/// Names of the rate-limit policies the Host applies directly when mounting
/// endpoint groups (OTLP ingestion, all read-API surfaces, /info, MCP). The
/// Dashboards module declares its own additional policies — see
/// <c>DashboardRateLimitPolicies</c>.
/// </summary>
internal static class HostRateLimitPolicies
{
    public const string OtlpIngest = "otlp-ingest";
    public const string ReadApi = "read-api";
}
