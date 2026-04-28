using OpenTelemetryDashboard.IntegrationTests.Fixtures;
using Xunit;

namespace OpenTelemetryDashboard.IntegrationTests.MultiProvider;

/// <summary>
/// Base class for cross-provider integration test fixtures. Owns the
/// IDatabaseFixture and ProviderTestHostFixture lifecycle, sets/clears the env
/// vars that Program.cs reads at boot (the WebApplicationFactory's
/// ConfigureAppConfiguration callback runs too late for the storage-provider
/// switch), and short-circuits via Skip if Docker is unavailable.
/// </summary>
public abstract class MultiProviderTestBase<TFixture> : IAsyncLifetime
    where TFixture : IDatabaseFixture, new()
{
    protected TFixture Db { get; } = new();
    protected ProviderTestHostFixture? Host { get; private set; }

    public async Task InitializeAsync()
    {
        Skip.IfNot(DockerAvailability.IsDockerAvailable, "Docker non disponibile");
        await Db.InitializeAsync();

        Environment.SetEnvironmentVariable("Dashboard__Storage__Provider", Db.ProviderName);
        Environment.SetEnvironmentVariable($"ConnectionStrings__{Db.ProviderName}", Db.ConnectionString);

        Host = new ProviderTestHostFixture(Db);
        _ = Host.Services;
        await Host.ApplyMigrationsAsync();
    }

    public async Task DisposeAsync()
    {
        if (Host is not null) await Host.DisposeAsync();
        Environment.SetEnvironmentVariable("Dashboard__Storage__Provider", null);
        Environment.SetEnvironmentVariable($"ConnectionStrings__{Db.ProviderName}", null);
        await Db.DisposeAsync();
    }
}
