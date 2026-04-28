namespace OpenTelemetryDashboard.Core.Abstractions.Retention;

/// <summary>
/// Deletes spans (and their owned events/links) older than a given age.
/// Resources referenced only by the deleted spans are left intact.
/// </summary>
public interface ITraceRetentionPolicy
{
    /// <summary>
    /// Removes every span whose start time is older than
    /// <paramref name="maxAge"/> relative to "now". Returns the number of
    /// span rows deleted.
    /// </summary>
    ValueTask<int> EnforceAsync(TimeSpan maxAge, CancellationToken cancellationToken);
}
