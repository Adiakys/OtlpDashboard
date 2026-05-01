using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.IntegrationTests.Fixtures;
using OpenTelemetryDashboard.Persistence;
using Xunit;

namespace OpenTelemetryDashboard.IntegrationTests.MultiProvider;

[CollectionDefinition("MultiProvider", DisableParallelization = true)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "xUnit collection definition pattern")]
public sealed class MultiProviderCollection;

[Collection("MultiProvider")]
public sealed class MigrationApplyOnPostgreSqlTests : MultiProviderTestBase<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public async Task Migrations_create_expected_tables_on_postgres()
    {
        await using var scope = Host!.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var context = await factory.CreateDbContextAsync();

        var tables = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT table_name
                FROM information_schema.tables
                WHERE table_schema = 'public'
                ORDER BY table_name
                """)
            .ToListAsync();

        tables.ShouldContain("resources");
        tables.ShouldContain("log_records");
        tables.ShouldContain("spans");
        tables.ShouldContain("span_events");
        tables.ShouldContain("span_links");
    }
}

[Collection("MultiProvider")]
public sealed class MigrationApplyOnSqlServerTests : MultiProviderTestBase<SqlServerDatabaseFixture>
{
    [SkippableFact]
    public async Task Migrations_create_expected_tables_on_sqlserver()
    {
        await using var scope = Host!.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var context = await factory.CreateDbContextAsync();

        var tables = await context.Database
            .SqlQueryRaw<string>(
                "SELECT name AS Value FROM sys.tables ORDER BY name")
            .ToListAsync();

        tables.ShouldContain("resources");
        tables.ShouldContain("log_records");
        tables.ShouldContain("spans");
        tables.ShouldContain("span_events");
        tables.ShouldContain("span_links");
    }
}
