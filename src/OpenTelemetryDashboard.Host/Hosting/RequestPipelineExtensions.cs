using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Host.Authentication;
using OpenTelemetryDashboard.Host.Configuration;

namespace OpenTelemetryDashboard.Host.Hosting;

/// <summary>
/// Configures the HTTP middleware pipeline in the order they must run:
/// rate-limiter, static SPA assets, authentication/authorization. The opt-in
/// auth posture warning is surfaced here too, so operators see the "tokens
/// not set" message immediately before any traffic hits the endpoints.
/// </summary>
internal static class RequestPipelineExtensions
{
    public static WebApplication UseDashboardPipeline(this WebApplication app)
    {
        // Run before everything else so static files, endpoints AND the
        // 401/429 short-circuit responses all carry the hardening headers.
        app.UseDashboardSecurityHeaders();

        app.UseRateLimiter();

        // Serve the Nuxt SPA (built with `nuxi generate`) from wwwroot/. In
        // dev the folder may be empty — Nuxt is served on its own port via
        // dev proxy; the static middleware just no-ops and requests fall
        // through to the endpoints. In prod the Dockerfile copies the SPA
        // output here.
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();

        LogAuthPosture(app);

        return app;
    }

    private static void LogAuthPosture(WebApplication app)
    {
        var auth = app.Services.GetRequiredService<IOptions<DashboardAuthOptions>>().Value;
        var logger = app.Services
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("Dashboard.Auth");

        if (string.IsNullOrEmpty(auth.BrowserToken))
        {
            logger.BrowserTokenNotSet();
        }
        if (string.IsNullOrEmpty(auth.Otlp.ApiKey))
        {
            logger.OtlpApiKeyNotSet();
        }
    }
}
