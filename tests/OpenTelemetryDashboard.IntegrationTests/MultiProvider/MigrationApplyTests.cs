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
public sealed class MigrationApplyOnPostgreSqlTests : IAsyncLifetime
{
    private readonly PostgreSqlDatabaseFixture _db = new();
    private ProviderTestHostFixture? _host;

    public async Task InitializeAsync()
    {
        Skip.IfNot(DockerAvailability.IsDockerAvailable, "Docker non disponibile");
        await _db.InitializeAsync();

        // Sovrascrivi via env var perché Program.cs legge il provider DIRETTAMENTE
        // da builder.Configuration al boot, prima che WebApplicationFactory applichi
        // gli override AddInMemoryCollection. Le env var vincono sui valori di
        // appsettings.json grazie all'EnvironmentVariablesConfigurationSource.
        Environment.SetEnvironmentVariable("Dashboard__Storage__Provider", _db.ProviderName);
        Environment.SetEnvironmentVariable($"ConnectionStrings__{_db.ProviderName}", _db.ConnectionString);

        _host = new ProviderTestHostFixture(_db);
        _ = _host.Services; // forza boot
        await _host.ApplyMigrationsAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
        Environment.SetEnvironmentVariable("Dashboard__Storage__Provider", null);
        Environment.SetEnvironmentVariable($"ConnectionStrings__{_db.ProviderName}", null);
        await _db.DisposeAsync();
    }

    [SkippableFact]
    public async Task Migrations_create_expected_tables_on_postgres()
    {
        await using var scope = _host!.Services.CreateAsyncScope();
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
public sealed class MigrationApplyOnSqlServerTests : IAsyncLifetime
{
    private readonly SqlServerDatabaseFixture _db = new();
    private ProviderTestHostFixture? _host;

    public async Task InitializeAsync()
    {
        Skip.IfNot(DockerAvailability.IsDockerAvailable, "Docker non disponibile");
        await _db.InitializeAsync();

        // Sovrascrivi via env var perché Program.cs legge il provider DIRETTAMENTE
        // da builder.Configuration al boot, prima che WebApplicationFactory applichi
        // gli override AddInMemoryCollection. Le env var vincono sui valori di
        // appsettings.json grazie all'EnvironmentVariablesConfigurationSource.
        Environment.SetEnvironmentVariable("Dashboard__Storage__Provider", _db.ProviderName);
        Environment.SetEnvironmentVariable($"ConnectionStrings__{_db.ProviderName}", _db.ConnectionString);

        _host = new ProviderTestHostFixture(_db);
        _ = _host.Services; // forza boot
        await _host.ApplyMigrationsAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null) await _host.DisposeAsync();
        Environment.SetEnvironmentVariable("Dashboard__Storage__Provider", null);
        Environment.SetEnvironmentVariable($"ConnectionStrings__{_db.ProviderName}", null);
        await _db.DisposeAsync();
    }

    [SkippableFact]
    public async Task Migrations_create_expected_tables_on_sqlserver()
    {
        await using var scope = _host!.Services.CreateAsyncScope();
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
