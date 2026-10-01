using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Host.Hosting;
using OpenTelemetryDashboard.IntegrationTests.Fixtures;
using OpenTelemetryDashboard.Persistence;
using OpenTelemetryDashboard.Persistence.PostgreSql;
using OpenTelemetryDashboard.Persistence.SqlServer;
using Xunit;

namespace OpenTelemetryDashboard.IntegrationTests.MultiProvider;

/// <summary>
/// Issue #37: all'avvio il database configurato può non esistere ancora.
/// Il lock di migrazione si connette al database di destinazione, quindi lo
/// startup deve crearlo prima di acquisire il lock.
/// </summary>
public abstract class MissingDatabaseStartupTests<TInner> : MultiProviderTestBase<MissingDatabaseFixture<TInner>>
    where TInner : IDatabaseFixture, new()
{
    protected abstract void AddTelemetryStore(IServiceCollection services, string connectionString);

    [SkippableFact]
    public async Task Startup_creates_missing_database_and_applies_migrations()
    {
        // L'host è già partito in InitializeAsync (Program → RunStartupAsync)
        // contro un database che non esisteva.
        await using var scope = Host!.Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var context = await factory.CreateDbContextAsync();

        (await context.Database.GetAppliedMigrationsAsync()).ShouldNotBeEmpty();
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [SkippableFact]
    public async Task Concurrent_replicas_create_missing_database_and_apply_migrations()
    {
        // Rolling deploy: più repliche partono insieme contro lo stesso
        // database mancante e corrono sul CREATE DATABASE.
        var connectionString = Db.CreateMissingDatabaseConnectionString();
        var replicas = Enumerable.Range(0, 4)
            .Select(_ =>
            {
                var services = new ServiceCollection();
                AddTelemetryStore(services, connectionString);
                return services.BuildServiceProvider();
            })
            .ToList();

        try
        {
            await Task.WhenAll(replicas.Select(sp => Task.Run(() => StartupBootstrap.ApplyMigrationsAsync(sp))));

            var factory = replicas[0].GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
            await using var context = await factory.CreateDbContextAsync();
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        }
        finally
        {
            foreach (var sp in replicas)
            {
                await sp.DisposeAsync();
            }
        }
    }
}

[Collection("MultiProvider")]
public sealed class MissingDatabaseStartupOnPostgreSqlTests : MissingDatabaseStartupTests<PostgreSqlDatabaseFixture>
{
    protected override void AddTelemetryStore(IServiceCollection services, string connectionString)
        => services.AddPostgreSqlTelemetryStore(connectionString);
}

[Collection("MultiProvider")]
public sealed class MissingDatabaseStartupOnSqlServerTests : MissingDatabaseStartupTests<SqlServerDatabaseFixture>
{
    protected override void AddTelemetryStore(IServiceCollection services, string connectionString)
        => services.AddSqlServerTelemetryStore(connectionString);
}
