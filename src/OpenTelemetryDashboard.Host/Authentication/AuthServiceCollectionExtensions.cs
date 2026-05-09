using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetryDashboard.Host.Configuration;

namespace OpenTelemetryDashboard.Host.Authentication;

public static class AuthServiceCollectionExtensions
{
    public const string ReadApiPolicy = "read-api";
    public const string OtlpIngestPolicy = "otlp-ingest";

    /// <summary>
    /// Registers the <see cref="StaticTokenAuthenticationHandler"/> scheme and
    /// the two authorization policies (<see cref="ReadApiPolicy"/> and
    /// <see cref="OtlpIngestPolicy"/>). Each policy degrades to allow-all when
    /// its corresponding token is not configured, preserving the "auth is
    /// opt-in" contract.
    /// </summary>
    public static IServiceCollection AddDashboardAuth(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services
            .AddOptions<DashboardAuthOptions>()
            .Bind(configuration.GetSection(DashboardAuthOptions.SectionName));

        services
            .AddAuthentication(StaticTokenAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, StaticTokenAuthenticationHandler>(
                StaticTokenAuthenticationHandler.SchemeName,
                _ => { });

        services.AddAuthorization(o =>
        {
            o.AddPolicy(
                ReadApiPolicy,
                BuildPolicy(
                    configuration,
                    environment,
                    $"{DashboardAuthOptions.SectionName}:{nameof(DashboardAuthOptions.BrowserToken)}",
                    StaticTokenAuthenticationHandler.RoleBrowser));

            o.AddPolicy(
                OtlpIngestPolicy,
                BuildPolicy(
                    configuration,
                    environment,
                    $"{DashboardAuthOptions.SectionName}:{nameof(DashboardAuthOptions.Otlp)}:{nameof(OtlpAuthOptions.ApiKey)}",
                    StaticTokenAuthenticationHandler.RoleOtlp));
        });

        return services;
    }

    private static Action<AuthorizationPolicyBuilder> BuildPolicy(
        IConfiguration configuration,
        IHostEnvironment environment,
        string configKey,
        string role)
    {
        return builder =>
        {
            // Allow-all is now an explicit opt-in (Dashboard:Auth:AllowAnonymous=true)
            // OR a Development-environment fallback for the convenience of local
            // dev / integration tests. Production with missing tokens is rejected
            // at boot by AuthPostureValidator before the policy is ever used.
            if (IsAnonymousAccessAllowed(configuration, environment, configKey))
            {
                builder.RequireAssertion(_ => true);
                return;
            }

            builder
                .AddAuthenticationSchemes(StaticTokenAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .RequireRole(role);
        };
    }

    /// <summary>
    /// True when the given policy must be allow-all: either the operator has
    /// explicitly opted in via <c>Dashboard:Auth:AllowAnonymous</c>, or we're
    /// running in Development with no token configured (the historical
    /// convenience for local dev / integration tests).
    /// </summary>
    internal static bool IsAnonymousAccessAllowed(
        IConfiguration configuration,
        IHostEnvironment environment,
        string configKey)
    {
        if (configuration.GetValue<bool>(
            $"{DashboardAuthOptions.SectionName}:Auth:{nameof(AuthPostureOptions.AllowAnonymous)}"))
        {
            return true;
        }

        var token = configuration[configKey];
        if (!string.IsNullOrEmpty(token))
        {
            return false;
        }

        return environment.IsDevelopment();
    }
}
