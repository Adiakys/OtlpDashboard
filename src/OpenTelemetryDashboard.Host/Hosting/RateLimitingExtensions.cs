using System.Threading.RateLimiting;
using OpenTelemetryDashboard.Dashboards;
using OpenTelemetryDashboard.Host.Configuration;

namespace OpenTelemetryDashboard.Host.Hosting;

/// <summary>
/// Registers the four rate-limit policies: per-IP sliding windows for
/// <see cref="HostRateLimitPolicies.OtlpIngest"/> /
/// <see cref="HostRateLimitPolicies.ReadApi"/> /
/// <see cref="DashboardRateLimitPolicies.Mutations"/>, plus a global
/// concurrency limiter for <see cref="DashboardRateLimitPolicies.PackInstall"/>.
/// Behind a reverse proxy the per-IP key collapses onto the proxy's address —
/// UseForwardedHeaders is the prerequisite for accurate keying once
/// X-Forwarded-For arrives.
/// </summary>
internal static class RateLimitingExtensions
{
    public static WebApplicationBuilder AddDashboardRateLimiting(this WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimitOptions(builder.Configuration);

        builder.Services.AddRateLimiter(rate =>
        {
            var limits = builder.Configuration
                .GetSection(RateLimitOptions.SectionName)
                .Get<RateLimitOptions>() ?? new RateLimitOptions();

            rate.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            rate.AddPolicy(HostRateLimitPolicies.OtlpIngest, PerIpSlidingWindow(limits.OtlpIngest, TimeSpan.FromSeconds(1)));
            rate.AddPolicy(HostRateLimitPolicies.ReadApi, PerIpSlidingWindow(limits.ReadApi, TimeSpan.FromSeconds(1)));
            rate.AddPolicy(DashboardRateLimitPolicies.Mutations, PerIpSlidingWindow(limits.Mutations, TimeSpan.FromSeconds(1)));
            rate.AddPolicy(DashboardRateLimitPolicies.PackInstall, PackInstallConcurrency(limits.PackInstall));
        });

        return builder;
    }

    private static Func<HttpContext, RateLimitPartition<string>> PerIpSlidingWindow(
        RateLimitBucket bucket,
        TimeSpan window) =>
        httpContext =>
        {
            var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetSlidingWindowLimiter(key, _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = bucket.PermitsPerSecond,
                Window = window,
                SegmentsPerWindow = 4,
                QueueLimit = bucket.Burst,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });
        };

    // pack-install fans out to a libgit2 clone and a recursive disk copy — both
    // expensive — so the primary defence is "one at a time, queue a few, reject
    // the rest". A per-IP throttle on top would help against many cooperating
    // clients but adds a second limiter; with concurrency=1 + queue=3 the 4th
    // concurrent call already 429s regardless of who sent it.
    private static Func<HttpContext, RateLimitPartition<string>> PackInstallConcurrency(
        PackInstallRateLimitOptions options) =>
        _ => RateLimitPartition.GetConcurrencyLimiter("pack-install:global", _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = options.MaxConcurrent,
            QueueLimit = options.ConcurrencyQueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        });
}
