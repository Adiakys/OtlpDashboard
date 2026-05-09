namespace OpenTelemetryDashboard.Dashboards;

/// <summary>
/// Names of the rate-limit policies the Dashboards module applies to its
/// mutating endpoints. The Host is responsible for registering matching
/// limiters under these names in <c>AddRateLimiter</c>.
/// </summary>
public static class DashboardRateLimitPolicies
{
    /// <summary>POST/PUT/DELETE on dashboards/widgets and pack uninstall.</summary>
    public const string Mutations = "dashboards:mutations";

    /// <summary>
    /// Pack install/update/reload: expensive (network + disk + libgit2). The
    /// Host wires this to a global concurrency limiter so a single in-flight
    /// clone can't be amplified by parallel callers.
    /// </summary>
    public const string PackInstall = "dashboards:pack-install";
}
