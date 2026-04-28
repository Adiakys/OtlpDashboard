using Testcontainers.MsSql;

namespace OpenTelemetryDashboard.IntegrationTests.Fixtures;

public sealed class SqlServerDatabaseFixture : IDatabaseFixture
{
    private readonly MsSqlContainer _container = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .WithPassword("Otel-Strong!2026")
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
