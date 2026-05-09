namespace OpenTelemetryDashboard.Persistence.Locking;

/// <summary>
/// Coordinates EF Core schema migrations across replicas. On a rolling
/// deploy multiple pods race to call <c>MigrateAsync</c> simultaneously;
/// without coordination two replicas can attempt the same DDL and fail
/// with a deadlock or partial-apply error. Implementations take a
/// distributed lock at the database level (Postgres advisory lock,
/// SQL Server sp_getapplock) so the second replica blocks until the
/// first finishes — at which point its own MigrateAsync becomes a no-op.
/// </summary>
/// <remarks>
/// SQLite needs no coordination: the file lock plus our WAL configuration
/// already serialise writers across processes, and the typical SQLite
/// deployment is a single instance anyway.
/// </remarks>
public interface IMigrationLock
{
    /// <summary>
    /// Block until the lock is held, then return a handle whose
    /// disposal releases it. Holders MUST dispose, even on exception.
    /// </summary>
    Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken);
}
