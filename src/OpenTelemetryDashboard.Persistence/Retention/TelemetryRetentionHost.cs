using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Core.Abstractions.Retention;

namespace OpenTelemetryDashboard.Persistence.Retention;

/// <summary>
/// Periodically evaluates the configured retention windows and delegates
/// deletion to the per-kind policies. A retention of <c>0</c> disables the
/// sweep for that kind. Failures on one kind do not affect the others.
/// </summary>
public sealed class TelemetryRetentionHost : BackgroundService
{
    private readonly ILogRetentionPolicy _logs;
    private readonly ITraceRetentionPolicy _traces;
    private readonly IMetricRetentionPolicy _metrics;
    private readonly IOptionsMonitor<TelemetryLimitsOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TelemetryRetentionHost> _logger;

    public TelemetryRetentionHost(
        ILogRetentionPolicy logs,
        ITraceRetentionPolicy traces,
        IMetricRetentionPolicy metrics,
        IOptionsMonitor<TelemetryLimitsOptions> options,
        TimeProvider timeProvider,
        ILogger<TelemetryRetentionHost> logger)
    {
        ArgumentNullException.ThrowIfNull(logs);
        ArgumentNullException.ThrowIfNull(traces);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _logs = logs;
        _traces = traces;
        _metrics = metrics;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromMinutes(_options.CurrentValue.SweepIntervalMinutes);
        using var timer = new PeriodicTimer(interval, _timeProvider);

        await SweepAsync(stoppingToken).ConfigureAwait(false);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        var current = _options.CurrentValue;

        // Each policy owns its own DbContext and writes to a disjoint table
        // set (Logs vs. Spans+SpanEvents+SpanLinks vs. MetricPoints+Instruments),
        // so the three sweeps can run concurrently. Failures are handled per
        // task inside RunAsync — Task.WhenAll therefore never throws.
        var tasks = new List<Task>(3);

        if (current.MaxLogDays > 0)
        {
            tasks.Add(RunAsync("logs", TimeSpan.FromDays(current.MaxLogDays),
                age => _logs.EnforceAsync(age, cancellationToken)));
        }

        if (current.MaxTraceDays > 0)
        {
            tasks.Add(RunAsync("traces", TimeSpan.FromDays(current.MaxTraceDays),
                age => _traces.EnforceAsync(age, cancellationToken)));
        }

        if (current.MaxMetricDays > 0)
        {
            tasks.Add(RunAsync("metrics", TimeSpan.FromDays(current.MaxMetricDays),
                age => _metrics.EnforceAsync(age, cancellationToken)));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private async Task RunAsync(string kind, TimeSpan maxAge, Func<TimeSpan, ValueTask<int>> action)
    {
        try
        {
            var removed = await action(maxAge).ConfigureAwait(false);
            if (removed > 0)
            {
                _logger.RetentionSweep(kind, removed, maxAge.TotalDays);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.RetentionFailed(ex, kind);
        }
    }
}

internal static partial class TelemetryRetentionHostLogs
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Information,
        Message = "Retention sweep removed {Removed} {Kind} record(s) older than {Days:F3} day(s)")]
    public static partial void RetentionSweep(this ILogger logger, string kind, int removed, double days);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "Retention sweep failed for {Kind}")]
    public static partial void RetentionFailed(this ILogger logger, Exception exception, string kind);
}
