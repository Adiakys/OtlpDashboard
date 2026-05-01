using Testcontainers.PostgreSql;

namespace OpenTelemetryDashboard.IntegrationTests.Fixtures;

public sealed class PostgreSqlDatabaseFixture : IDatabaseFixture
{
    // `WithReuse(true)` keeps the container alive across `dotnet test`
    // invocations: the second run skips the ~3s startup. Testcontainers
    // hashes the builder config to find a match, so the labels below are
    // deliberately stable.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("oteldashboard_test")
        .WithUsername("otel")
        .WithPassword("otel")
        .WithReuse(true)
        .WithLabel("oteldash.testcontainer", "postgres")
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
