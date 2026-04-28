namespace OpenTelemetryDashboard.Core.Abstractions.Retention;

/// <summary>
/// Drops metric data points older than a given age from the backing store.
/// Instruments whose points are all expired may be removed entirely.
/// </summary>
public interface IMetricRetentionPolicy
{
    /// <summary>
    /// Removes every data point whose timestamp is older than
    /// <paramref name="maxAge"/> relative to "now". Returns the number of
    /// points dropped across all instruments.
    /// </summary>
    ValueTask<int> EnforceAsync(TimeSpan maxAge, CancellationToken cancellationToken);
}
