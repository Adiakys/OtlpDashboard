using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.IntegrationTests.Fixtures;

/// <summary>
/// WebApplicationFactory parametrizzato per provider. Riceve un IDatabaseFixture
/// e ne usa la connection string + provider name nella configurazione del host.
/// </summary>
/// <remarks>
/// Ownership: questa classe NON dispone <see cref="Database"/>. La test class
/// che usa il fixture è responsabile di implementare <c>IAsyncLifetime</c> e
/// chiamare <c>DisposeAsync()</c> sia su <see cref="ProviderTestHostFixture"/>
/// che sul <see cref="IDatabaseFixture"/> sottostante.
/// </remarks>
public sealed class ProviderTestHostFixture : WebApplicationFactory<Program>
{
    public ProviderTestHostFixture(IDatabaseFixture databaseFixture)
    {
        ArgumentNullException.ThrowIfNull(databaseFixture);
        Database = databaseFixture;
    }

    public IDatabaseFixture Database { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dashboard:Storage:Provider"] = Database.ProviderName,
                [Database.ConnectionStringConfigKey] = Database.ConnectionString,
                ["OpenTelemetryDashboard:Ingestion:Channel:Capacity"] = "1000",
                ["OpenTelemetryDashboard:Ingestion:Channel:MaxBatchSize"] = "64",
                ["OpenTelemetryDashboard:Ingestion:Channel:FlushIntervalMs"] = "50",
            });
        });
    }

    public async Task ApplyMigrationsAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        await context.Database.MigrateAsync();
    }
}
