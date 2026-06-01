using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Host.Authentication;
using OpenTelemetryDashboard.Host.Configuration;

namespace OpenTelemetryDashboard.Host.Hosting;

/// <summary>
/// Configures the HTTP middleware pipeline in the order they must run:
/// rate-limiter, static SPA assets, authentication/authorization. The auth
/// posture warning is surfaced here too, so operators see the "tokens not
/// set — that surface is public" message before any traffic hits the
/// endpoints.
/// </summary>
internal static class RequestPipelineExtensions
{
    public static WebApplication UseDashboardPipeline(this WebApplication app)
    {
        var auth = app.Services.GetRequiredService<IOptions<DashboardAuthOptions>>().Value;

        // Outermost catch-all: anything that escapes a downstream middleware
        // or endpoint as an unhandled exception is reshaped into a uniform
        // RFC 7807 ProblemDetails response by GlobalExceptionHandler. Must
        // be first so it sees exceptions from every later component.
        app.UseExceptionHandler();

        // Run before everything else so static files, endpoints AND the
        // 401/429 short-circuit responses all carry the hardening headers.
        // Headers are queued via OnStarting so they survive the
        // Response.Clear() the exception handler does on its way to a 500.
        app.UseDashboardSecurityHeaders();

        app.UseRateLimiter();

        // Serve the Nuxt SPA (built with `nuxi generate`) from wwwroot/. In
        // dev the folder may be empty — Nuxt is served on its own port via
        // dev proxy; the static middleware just no-ops and requests fall
        // through to the endpoints. In prod the Dockerfile copies the SPA
        // output here.
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // CORS sits before auth so preflight (OPTIONS) responses don't get
        // bounced by the 401 short-circuit. The default policy is empty
        // unless Dashboard:Cors:AllowedOrigins is populated.
        app.UseCors();

        app.UseAuthentication();
        app.UseAuthorization();

        LogAuthPosture(app, auth);

        return app;
    }

    private static void LogAuthPosture(WebApplication app, DashboardAuthOptions auth)
    {
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
