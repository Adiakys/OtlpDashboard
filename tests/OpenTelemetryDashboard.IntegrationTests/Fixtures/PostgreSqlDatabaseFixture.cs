using Testcontainers.PostgreSql;

namespace OpenTelemetryDashboard.IntegrationTests.Fixtures;

public sealed class PostgreSqlDatabaseFixture : IDatabaseFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("oteldashboard_test")
        .WithUsername("otel")
        .WithPassword("otel")
        .Build();

    public string ProviderName => "PostgreSql";
    public string ConnectionStringConfigKey => "ConnectionStrings:PostgreSql";
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
