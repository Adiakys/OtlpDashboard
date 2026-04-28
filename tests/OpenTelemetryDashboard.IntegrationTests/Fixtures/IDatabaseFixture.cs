namespace OpenTelemetryDashboard.IntegrationTests.Fixtures;

/// <summary>
/// Astrazione su un DB di test specifico per provider. Implementazioni:
/// SqliteDatabaseFixture (file effimero), PostgreSqlDatabaseFixture e
/// SqlServerDatabaseFixture (entrambi via Testcontainers, container effimero
/// per fixture).
/// </summary>
public interface IDatabaseFixture : IAsyncLifetime
{
    string ProviderName { get; }
    string ConnectionString { get; }
    string ConnectionStringConfigKey { get; }
}
