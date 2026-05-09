namespace OpenTelemetryDashboard.Persistence.Ingestion;

/// <summary>
/// Bounded exponential-backoff retry around a flaky write. Default policy
/// is 3 attempts at 100ms / 300ms / 800ms = ~1.2s worst case before
/// giving up and surfacing the last exception to the caller.
/// </summary>
/// <remarks>
/// We retry on <em>any</em> exception (apart from cancellation) on purpose:
/// the canonical transient failures we see — SQLite <c>SQLITE_BUSY</c>
/// surviving the busy_timeout, the ResourceUpserter PK race between two
/// concurrent sinks, brief Postgres/SqlServer connection blips — all
/// resolve themselves by the second or third attempt. Deterministic
/// failures (constraint violations, schema drift) survive the retries
/// and burn ~1.2s of latency on the dropped batch — a price worth paying
/// for not having to enumerate every provider's transient error code.
/// </remarks>
internal static class BoundedRetry
{
    public static readonly IReadOnlyList<TimeSpan> DefaultDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(800),
    ];

    public static async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken,
        IReadOnlyList<TimeSpan>? delays = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        delays ??= DefaultDelays;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await action(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch when (attempt < delays.Count)
            {
                await Task.Delay(delays[attempt], cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
