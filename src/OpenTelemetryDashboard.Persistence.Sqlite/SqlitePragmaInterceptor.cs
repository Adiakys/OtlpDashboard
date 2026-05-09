using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OpenTelemetryDashboard.Persistence.Sqlite;

/// <summary>
/// Sets SQLite session/file-level PRAGMAs on every newly-opened connection so
/// the database is production-viable under sustained ingest:
/// <list type="bullet">
///   <item><c>journal_mode = WAL</c> — readers no longer block on writers (default
///   <c>delete</c> serialises the entire DB across all connections). Persistent.</item>
///   <item><c>synchronous = NORMAL</c> — paired with WAL this is the recommended
///   durability/throughput trade-off.</item>
///   <item><c>busy_timeout = 5000</c> — wait up to 5s on a held lock instead of
///   surfacing <c>SQLITE_BUSY</c> to the caller.</item>
///   <item><c>foreign_keys = ON</c> — SQLite's default is OFF; we model FKs in the
///   schema and rely on them.</item>
///   <item><c>temp_store = MEMORY</c> — keep temp B-trees off disk.</item>
/// </list>
/// All PRAGMAs are idempotent so re-running them on a pooled connection is safe.
/// In-memory SQLite databases ignore <c>journal_mode = WAL</c> (they stay in
/// MEMORY mode) — the statement still succeeds.
/// </summary>
internal sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string PragmaScript = """
        PRAGMA journal_mode = WAL;
        PRAGMA synchronous = NORMAL;
        PRAGMA busy_timeout = 5000;
        PRAGMA foreign_keys = ON;
        PRAGMA temp_store = MEMORY;
        """;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = PragmaScript;
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = PragmaScript;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
