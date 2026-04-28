namespace OpenTelemetryDashboard.IntegrationTests.Fixtures;

public sealed class SqliteDatabaseFixture : IDatabaseFixture
{
    private string _databasePath = string.Empty;

    public string ProviderName => "Sqlite";
    public string ConnectionStringConfigKey => "ConnectionStrings:Sqlite";
    public string ConnectionString { get; private set; } = string.Empty;

    public Task InitializeAsync()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"oteldash-test-{Guid.NewGuid():N}.db");
        ConnectionString = $"Data Source={_databasePath}";
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch (IOException) { /* file ancora locked: ignoriamo */ }
        catch (UnauthorizedAccessException) { /* file in uso (Windows): ignoriamo */ }
        return Task.CompletedTask;
    }
}
