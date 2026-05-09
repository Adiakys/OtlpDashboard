using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Host.Configuration;

namespace OpenTelemetryDashboard.Host.Hosting;

/// <summary>
/// Translates <see cref="DashboardCorsOptions"/> (the operator-facing
/// section under <c>Dashboard:Cors</c>) into the framework's
/// <see cref="CorsOptions"/> default policy. Defers the read until DI
/// resolution time so test hosts that append in-memory configuration
/// sources after <c>Program.cs</c> still see their values.
/// </summary>
internal sealed class ConfigureCorsFromDashboard : IConfigureOptions<CorsOptions>
{
    private readonly IOptions<DashboardCorsOptions> _dashboardCors;

    public ConfigureCorsFromDashboard(IOptions<DashboardCorsOptions> dashboardCors)
    {
        _dashboardCors = dashboardCors;
    }

    public void Configure(CorsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var origins = _dashboardCors.Value.AllowedOrigins;
        options.AddDefaultPolicy(policy =>
        {
            if (origins.Length == 0) return;

            policy
                .WithOrigins(origins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                // The dashboard's auth cookie is HttpOnly + SameSite=Strict;
                // a cross-origin SPA needs AllowCredentials() to attach it.
                .AllowCredentials();
        });
    }
}
