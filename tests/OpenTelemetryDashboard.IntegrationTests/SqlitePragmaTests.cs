using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.IntegrationTests;

/// <summary>
/// Verifies that the SQLite provider applies the production-viability PRAGMAs
/// (WAL, busy_timeout, foreign_keys, …) on every connection it hands out.
/// </summary>
public sealed class SqlitePragmaTests : IClassFixture<TestHostFixture>
{
    private readonly TestHostFixture _host;

    public SqlitePragmaTests(TestHostFixture host)
    {
        _host = host;
    }

    [Theory]
    [InlineData("journal_mode", "wal")]
    [InlineData("synchronous", "1")]      // 1 = NORMAL
    [InlineData("busy_timeout", "5000")]
    [InlineData("foreign_keys", "1")]
    [InlineData("temp_store", "2")]       // 2 = MEMORY
    public async Task Sqlite_pragma_is_applied_on_every_connection(string pragma, string expected)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();

        var connection = ctx.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA {pragma};";
        var actual = (await cmd.ExecuteScalarAsync())?.ToString();

        Assert.Equal(expected, actual, ignoreCase: true);
    }
}
