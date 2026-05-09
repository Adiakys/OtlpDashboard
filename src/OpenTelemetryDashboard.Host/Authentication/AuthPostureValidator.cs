using Microsoft.Extensions.Hosting;
using OpenTelemetryDashboard.Host.Configuration;

namespace OpenTelemetryDashboard.Host.Authentication;

/// <summary>
/// Boot-time check that refuses to start the host in Production with empty
/// tokens unless the operator explicitly opted in to public access via
/// <c>Dashboard:Auth:AllowAnonymous=true</c>. Replaces the historical
/// "log a warning and silently allow everyone" posture, which let a
/// missing env var ship a fully-public dashboard to production.
/// </summary>
internal static class AuthPostureValidator
{
    public static void Validate(DashboardAuthOptions options, IHostEnvironment env)
    {
        if (env.IsDevelopment())
        {
            // Development: empty tokens are tolerated for local-dev UX. The
            // existing startup warning (see RequestPipelineExtensions) still
            // surfaces the posture in logs.
            return;
        }

        if (options.Auth.AllowAnonymous)
        {
            // Operator explicitly opted in. The AuthPostureHealthCheck still
            // marks /healthz as Degraded so the orchestrator can surface it.
            return;
        }

        var missing = new List<string>();
        if (string.IsNullOrEmpty(options.BrowserToken)) missing.Add("Dashboard:BrowserToken");
        if (string.IsNullOrEmpty(options.Otlp.ApiKey)) missing.Add("Dashboard:Otlp:ApiKey");

        if (missing.Count == 0) return;

        throw new InvalidOperationException(
            $"Auth is required in {env.EnvironmentName} but the following tokens are not configured: " +
            $"{string.Join(", ", missing)}.\n\n" +
            "Set both tokens via configuration / environment variables " +
            "(DASHBOARD__BROWSERTOKEN, DASHBOARD__OTLP__APIKEY), OR opt in explicitly to public access:\n\n" +
            "  Dashboard:Auth:AllowAnonymous=true\n\n" +
            "This switch makes every API and the OTLP ingest endpoint public — " +
            "set it only for air-gapped / private-network deployments.");
    }
}
