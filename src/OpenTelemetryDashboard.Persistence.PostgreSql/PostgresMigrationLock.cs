using Npgsql;
using OpenTelemetryDashboard.Persistence.Locking;

namespace OpenTelemetryDashboard.Persistence.PostgreSql;

/// <summary>
/// <see cref="IMigrationLock"/> backed by Postgres' session-scoped advisory
/// locks. <c>pg_advisory_lock</c> blocks until the key is free, then holds
/// it for the lifetime of the connection — closing the connection releases
/// it automatically, so an aborted process can't leave a stuck lock.
/// </summary>
internal sealed class PostgresMigrationLock : IMigrationLock
{
    // Stable arbitrary 64-bit key. Chosen randomly once and pinned: every
    // replica racing on this database must pick the same value, and we
    // want to avoid colliding with locks taken by application code or
    // other tools sharing the cluster.
    private const long LockKey = 0x4F54454C44415348L; // "OTELDASH"

    private readonly string _connectionString;

    public PostgresMigrationLock(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        var conn = new NpgsqlConnection(_connectionString);
        try
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT pg_advisory_lock($1)";
            cmd.Parameters.Add(new NpgsqlParameter { Value = LockKey });
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await conn.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        return new Handle(conn);
    }

    private sealed class Handle : IAsyncDisposable
    {
        private readonly NpgsqlConnection _conn;
        public Handle(NpgsqlConnection conn) => _conn = conn;

        public async ValueTask DisposeAsync()
        {
            // Closing the connection auto-releases session-level advisory
            // locks; pg_advisory_unlock here is best-effort cleanliness.
            try
            {
                await using var cmd = _conn.CreateCommand();
                cmd.CommandText = "SELECT pg_advisory_unlock($1)";
                cmd.Parameters.Add(new NpgsqlParameter { Value = LockKey });
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch
            {
                // Connection already in a bad state: dispose still releases.
            }
            await _conn.DisposeAsync().ConfigureAwait(false);
        }
    }
}
