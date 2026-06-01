using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Host.Configuration;

namespace OpenTelemetryDashboard.Host.Authentication;

/// <summary>
/// Surfaces the auth posture through <c>/healthz</c>: when either token is
/// unset (so that surface is public), in any environment, the check returns
/// <see cref="HealthStatus.Degraded"/> so an SRE looking at the orchestrator
/// dashboard sees the posture without scraping logs.
/// </summary>
internal sealed class AuthPostureHealthCheck : IHealthCheck
{
    private readonly IOptionsMonitor<DashboardAuthOptions> _options;

    public AuthPostureHealthCheck(IOptionsMonitor<DashboardAuthOptions> options)
    {
        _options = options;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var current = _options.CurrentValue;
        var data = new Dictionary<string, object>
        {
            ["browserTokenConfigured"] = !string.IsNullOrEmpty(current.BrowserToken),
            ["otlpApiKeyConfigured"] = !string.IsNullOrEmpty(current.Otlp.ApiKey),
        };

        if (string.IsNullOrEmpty(current.BrowserToken) || string.IsNullOrEmpty(current.Otlp.ApiKey))
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "One or more tokens are unset — the affected endpoints are public.",
                data: data));
        }
        return Task.FromResult(HealthCheckResult.Healthy("Auth tokens configured.", data: data));
    }
}
