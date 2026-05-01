using OpenTelemetryDashboard.IntegrationTests.Fixtures;
using Xunit;

namespace OpenTelemetryDashboard.IntegrationTests.MultiProvider;

/// <summary>
/// Base class for cross-provider integration test fixtures. Owns the
/// IDatabaseFixture and ProviderTestHostFixture lifecycle, sets/clears the env
/// vars that Program.cs reads at boot (the WebApplicationFactory's
/// ConfigureAppConfiguration callback runs too late for the storage-provider
/// switch), and short-circuits via Skip if Docker is unavailable.
///
/// <para>
/// <b>Opt-in:</b> these tests pull a Postgres or SQL Server container per
/// fixture (~3s and ~10s respectively) and the suite runs sequentially via
/// <c>[CollectionDefinition(DisableParallelization = true)]</c>, so the
/// full multi-provider sweep adds ~70–90s to a test run. To keep the
/// dev-loop fast they're skipped unless the
/// <c>INTEGRATION_TESTS_MULTIPROVIDER=true</c> env var is set — CI sets
/// it explicitly, local runs default to skip. Run ad-hoc with
/// <c>INTEGRATION_TESTS_MULTIPROVIDER=true dotnet test</c>.
/// </para>
/// </summary>
public abstract class MultiProviderTestBase<TFixture> : IAsyncLifetime
    where TFixture : IDatabaseFixture, new()
{
    private const string OptInEnvVar = "INTEGRATION_TESTS_MULTIPROVIDER";

    protected TFixture Db { get; } = new();
    protected ProviderTestHostFixture? Host { get; private set; }

    public async Task InitializeAsync()
    {
        var optIn = Environment.GetEnvironmentVariable(OptInEnvVar);
        Skip.IfNot(
            string.Equals(optIn, "true", StringComparison.OrdinalIgnoreCase) || optIn == "1",
            $"Multi-provider tests skipped. Set {OptInEnvVar}=true to run them.");
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
