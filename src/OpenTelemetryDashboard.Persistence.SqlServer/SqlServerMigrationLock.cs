using System.Data;
using Microsoft.Data.SqlClient;
using OpenTelemetryDashboard.Persistence.Locking;

namespace OpenTelemetryDashboard.Persistence.SqlServer;

/// <summary>
/// <see cref="IMigrationLock"/> backed by SQL Server's <c>sp_getapplock</c>
/// in session mode. The lock is bound to the session and released either by
/// matching <c>sp_releaseapplock</c> or — implicitly — when the session ends.
/// Closing our dedicated connection at <c>DisposeAsync</c> guarantees release
/// even if the process crashes.
/// </summary>
internal sealed class SqlServerMigrationLock : IMigrationLock
{
    private const string ResourceName = "oteldash-migrate";
    private const int LockTimeoutMs = 60_000;

    private readonly string _connectionString;

    public SqlServerMigrationLock(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        var conn = new SqlConnection(_connectionString);
        try
        {
            await conn.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "sp_getapplock";
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add(new SqlParameter("@Resource", ResourceName));
            cmd.Parameters.Add(new SqlParameter("@LockMode", "Exclusive"));
            cmd.Parameters.Add(new SqlParameter("@LockOwner", "Session"));
            cmd.Parameters.Add(new SqlParameter("@LockTimeout", LockTimeoutMs));
            // Return code: ≥0 success, <0 failure (-1 timeout, -2 cancelled, …).
            var rc = new SqlParameter
            {
                Direction = ParameterDirection.ReturnValue,
                SqlDbType = SqlDbType.Int,
            };
            cmd.Parameters.Add(rc);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if ((int)rc.Value < 0)
            {
                throw new InvalidOperationException(
                    $"sp_getapplock returned {rc.Value}; could not acquire migration lock '{ResourceName}'.");
            }
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
        private readonly SqlConnection _conn;
        public Handle(SqlConnection conn) => _conn = conn;

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var cmd = _conn.CreateCommand();
                cmd.CommandText = "sp_releaseapplock";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(new SqlParameter("@Resource", ResourceName));
                cmd.Parameters.Add(new SqlParameter("@LockOwner", "Session"));
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch
            {
                // Closing the session releases the lock regardless.
            }
            await _conn.DisposeAsync().ConfigureAwait(false);
        }
    }
}
