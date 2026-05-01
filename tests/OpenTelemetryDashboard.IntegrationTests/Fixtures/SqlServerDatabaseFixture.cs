using Testcontainers.MsSql;

namespace OpenTelemetryDashboard.IntegrationTests.Fixtures;

public sealed class SqlServerDatabaseFixture : IDatabaseFixture
{
    // SQL Server 2022 cold start is ~10s — `WithReuse(true)` makes
    // subsequent `dotnet test` runs skip it entirely.
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Otel-Strong!2026")
        .WithReuse(true)
        .WithLabel("oteldash.testcontainer", "sqlserver")
        .Build();

    public string ProviderName => "SqlServer";
    public string ConnectionStringConfigKey => "ConnectionStrings:SqlServer";
    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}
