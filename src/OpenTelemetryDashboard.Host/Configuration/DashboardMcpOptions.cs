namespace OpenTelemetryDashboard.Host.Configuration;

/// <summary>
/// MCP server config. Bound from <c>Dashboard:Mcp</c>
/// (environment variable <c>DASHBOARD__MCP__ENABLED</c>).
/// <para>
/// Disabled by default. When <see cref="Enabled"/> is <c>false</c> no MCP
/// services are registered and the <c>/mcp</c> route is not mapped — the
/// SDK and its dependencies remain dormant. When <c>true</c> the endpoint
/// is gated by the same read-API authorization policy that protects
/// <c>/api/v1/*</c>, i.e. it accepts the configured browser bearer token.
/// </para>
/// </summary>
public sealed class DashboardMcpOptions
{
    public const string SectionName = "Dashboard:Mcp";

    public bool Enabled { get; set; }
}
