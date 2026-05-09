using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace OpenTelemetryDashboard.Host.Configuration;

/// <summary>
/// Per-policy rate-limit configuration. Each bucket is partitioned by client
/// IP at runtime (see <c>Program.cs</c>). Tune in <c>appsettings.json</c> under
/// <c>Dashboard:RateLimits</c>.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "Dashboard:RateLimits";

    public RateLimitBucket OtlpIngest { get; set; } = new()
    {
        PermitsPerSecond = 200,
        Burst = 500,
    };

    public RateLimitBucket ReadApi { get; set; } = new()
    {
        PermitsPerSecond = 60,
        Burst = 120,
    };

    public RateLimitBucket Mutations { get; set; } = new()
    {
        PermitsPerSecond = 10,
        Burst = 30,
    };

    public PackInstallRateLimitOptions PackInstall { get; set; } = new();
}

public sealed class RateLimitBucket
{
    [Range(1, 1_000_000)]
    public int PermitsPerSecond { get; set; } = 100;

    [Range(0, 1_000_000)]
    public int Burst { get; set; } = 200;
}

/// <summary>
/// Pack-install combines a per-IP throttle (so a single client can't fan out
/// installs) with a global concurrency cap (so a single slow git clone can't
/// tie up multiple workers).
/// </summary>
public sealed class PackInstallRateLimitOptions
{
    [Range(1, 10_000)]
    public int PermitsPerMinute { get; set; } = 5;

    [Range(0, 10_000)]
    public int Burst { get; set; } = 3;

    /// <summary>Maximum simultaneous install/update operations across all clients.</summary>
    [Range(1, 1_000)]
    public int MaxConcurrent { get; set; } = 1;

    /// <summary>How many requests can wait for a free slot when MaxConcurrent is saturated.</summary>
    [Range(0, 10_000)]
    public int ConcurrencyQueueLimit { get; set; } = 3;
}

public static class RateLimitOptionsExtensions
{
    public static IServiceCollection AddRateLimitOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
