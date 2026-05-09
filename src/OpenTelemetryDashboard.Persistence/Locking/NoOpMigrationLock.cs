namespace OpenTelemetryDashboard.Persistence.Locking;

/// <summary>
/// Default <see cref="IMigrationLock"/> for providers that don't need
/// distributed coordination. Used by SQLite (file lock + WAL already
/// serialise writers) and as a fallback before a provider-specific
/// implementation is registered.
/// </summary>
internal sealed class NoOpMigrationLock : IMigrationLock
{
    private static readonly Task<IAsyncDisposable> Acquired = Task.FromResult<IAsyncDisposable>(NoOpHandle.Instance);

    public Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken) => Acquired;

    private sealed class NoOpHandle : IAsyncDisposable
    {
        public static readonly NoOpHandle Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
