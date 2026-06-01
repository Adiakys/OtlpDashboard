using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Host.Configuration;

namespace OpenTelemetryDashboard.Host.Authentication;

public static class AuthServiceCollectionExtensions
{
    public const string ReadApiPolicy = "read-api";
    public const string OtlpIngestPolicy = "otlp-ingest";

    public static IServiceCollection AddDashboardAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

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
                    $"{DashboardAuthOptions.SectionName}:{nameof(DashboardAuthOptions.BrowserToken)}",
                    StaticTokenAuthenticationHandler.RoleBrowser));

            o.AddPolicy(
                OtlpIngestPolicy,
                BuildPolicy(
                    configuration,
                    $"{DashboardAuthOptions.SectionName}:{nameof(DashboardAuthOptions.Otlp)}:{nameof(OtlpAuthOptions.ApiKey)}",
                    StaticTokenAuthenticationHandler.RoleOtlp));
        });

        return services;
    }

    private static Action<AuthorizationPolicyBuilder> BuildPolicy(
        IConfiguration configuration,
        string configKey,
        string role)
    {
        return builder =>
        {
            if (IsAnonymousAccessAllowed(configuration, configKey))
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

    internal static bool IsAnonymousAccessAllowed(IConfiguration configuration, string configKey)
    {
        return string.IsNullOrEmpty(configuration[configKey]);
    }
}
