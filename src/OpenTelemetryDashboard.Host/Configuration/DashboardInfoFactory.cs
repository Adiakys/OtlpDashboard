using System.Reflection;

using Microsoft.Extensions.Options;

using OpenTelemetryDashboard.Api;
using OpenTelemetryDashboard.Api.Contracts;
using OpenTelemetryDashboard.Persistence.Retention;

namespace OpenTelemetryDashboard.Host.Configuration;

/// <summary>
/// Builds the fully-populated <see cref="DashboardInfoDto"/> the Host
/// registers as a singleton at startup. Encapsulates assembly-version
/// resolution so the Host doesn't have to reach into Api internals to
/// surface a build identifier.
/// <para>
/// The Host wires this once during <c>Program.cs</c>:
/// <code>
/// builder.Services.AddSingleton(_ => DashboardInfoFactory.BuildFull(
///     applicationName, storageProvider, telemetryLimits));
/// </code>
/// The endpoint then receives the full record from DI and gates each
/// auth-sensitive field via a <c>with</c> redaction.
/// </para>
/// </summary>
public static class DashboardInfoFactory
{
    public static IServiceCollection RegisterDashboardInfo(this IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var options = BuildDashboardInfoDto(sp);
            return Options.Create(options);
        });
        return services;
    }
    
    private static DashboardInfoDto BuildDashboardInfoDto(IServiceProvider sp)
    {
        var info = sp.GetRequiredService<IOptions<DashboardInfoOptions>>().Value;
        var storage = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
        var limits = sp.GetRequiredService<IOptions<TelemetryLimitsOptions>>().Value;
        var queryApi = sp.GetRequiredService<IOptions<QueryApiOptions>>().Value;
        var auth = sp.GetRequiredService<IOptions<DashboardAuthOptions>>().Value;

        return new DashboardInfoDto(info.ApplicationName)
        {
            RequireAuth = !string.IsNullOrEmpty(auth.BrowserToken),
            Version = ResolveVersion(),
            StorageProvider = storage.Provider.ToString(),
            TelemetryLimits = new TelemetryLimitsDto(
                    MaxLogDays: limits.MaxLogDays,
                    MaxTraceDays: limits.MaxTraceDays,
                    MaxMetricDays: limits.MaxMetricDays,
                    SweepIntervalMinutes: limits.SweepIntervalMinutes
                ),
            QueryLimits = new QueryLimitsDto(
                MaxWindowHours: queryApi.MaxWindowHours,
                MaxLimit: queryApi.MaxLimit
            ),
        };
    }

    /// <summary>
    /// Reads <see cref="AssemblyInformationalVersionAttribute"/> off the Api
    /// assembly. Anchored to a known assembly (not <c>Assembly.GetEntryAssembly</c>)
    /// so test hosts running under <c>WebApplicationFactory</c> don't leak
    /// the test runner's version into production traffic.
    /// </summary>
    private static string ResolveVersion()
    {
        var assembly = typeof(DashboardInfoFactory).Assembly;
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrEmpty(informational)) return informational;

        var assemblyVersion = assembly.GetName().Version?.ToString();
        return string.IsNullOrEmpty(assemblyVersion) ? "unknown" : assemblyVersion;
    }
}
