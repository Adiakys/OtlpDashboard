namespace OpenTelemetryDashboard.Core.Abstractions.Retention;

/// <summary>
/// Deletes log records older than a given age from the backing store.
/// </summary>
public interface ILogRetentionPolicy
{
    /// <summary>
    /// Removes every log record whose observation time is older than
    /// <paramref name="maxAge"/> relative to "now". Returns the number of
    /// rows deleted. Safe to call repeatedly; a no-op if nothing matches.
    /// </summary>
    ValueTask<int> EnforceAsync(TimeSpan maxAge, CancellationToken cancellationToken);
}
