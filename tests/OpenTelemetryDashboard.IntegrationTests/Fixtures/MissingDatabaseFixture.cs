using Microsoft.Data.SqlClient;
using Npgsql;

namespace OpenTelemetryDashboard.IntegrationTests.Fixtures;

/// <summary>
/// Riusa il container di <typeparamref name="TInner"/> ma punta la connection
/// string a un database che non esiste ancora, per verificare che lo startup
/// lo crei (issue #37). I database generati vengono droppati in
/// <see cref="DisposeAsync"/>: i container sono in reuse tra un run e l'altro.
/// </summary>
public sealed class MissingDatabaseFixture<TInner> : IDatabaseFixture
    where TInner : IDatabaseFixture, new()
{
    private readonly TInner _inner = new();
    private readonly List<string> _databaseNames = [];
    private string? _connectionString;

    public string ProviderName => _inner.ProviderName;
    public string ConnectionStringConfigKey => _inner.ConnectionStringConfigKey;
    public string ConnectionString => _connectionString ??= CreateMissingDatabaseConnectionString();

    /// <summary>
    /// Connection string verso un nuovo database (nome univoco) non ancora creato.
    /// </summary>
    public string CreateMissingDatabaseConnectionString()
    {
        var name = $"oteldash_missing_{Guid.NewGuid():N}";
        _databaseNames.Add(name);
        return WithDatabase(name);
    }

    public Task InitializeAsync() => _inner.InitializeAsync();

    public async Task DisposeAsync()
    {
        foreach (var name in _databaseNames)
        {
            await DropDatabaseAsync(name);
        }
        await _inner.DisposeAsync();
    }

    private string WithDatabase(string name) => ProviderName switch
    {
        "PostgreSql" => new NpgsqlConnectionStringBuilder(_inner.ConnectionString) { Database = name }.ConnectionString,
        "SqlServer" => new SqlConnectionStringBuilder(_inner.ConnectionString) { InitialCatalog = name }.ConnectionString,
        _ => throw new NotSupportedException($"Provider '{ProviderName}' non supportato."),
    };

    private async Task DropDatabaseAsync(string name)
    {
        // `name` è generato da noi (prefisso + GUID hex): sicuro da interpolare.
        switch (ProviderName)
        {
            case "PostgreSql":
                NpgsqlConnection.ClearAllPools();
                await using (var conn = new NpgsqlConnection(WithDatabase("postgres")))
                {
                    await conn.OpenAsync();
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)";
                    await cmd.ExecuteNonQueryAsync();
                }
                break;
            case "SqlServer":
                SqlConnection.ClearAllPools();
                await using (var conn = new SqlConnection(WithDatabase("master")))
                {
                    await conn.OpenAsync();
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"""
                        IF DB_ID(N'{name}') IS NOT NULL
                        BEGIN
                            ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                            DROP DATABASE [{name}];
                        END
                        """;
                    await cmd.ExecuteNonQueryAsync();
                }
                break;
        }
    }
}
