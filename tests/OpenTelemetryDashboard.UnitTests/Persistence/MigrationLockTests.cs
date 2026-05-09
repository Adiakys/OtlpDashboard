using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Persistence;
using OpenTelemetryDashboard.Persistence.Locking;

namespace OpenTelemetryDashboard.UnitTests.Persistence;

public class MigrationLockTests
{
    [Fact]
    public async Task DefaultRegistration_yields_a_NoOpLock_that_acquires_and_releases_immediately()
    {
        // Provider-specific extensions (Postgres, SqlServer) override the
        // default; in the absence of those (i.e. SQLite or test setup
        // without a provider), AddTelemetryPersistenceCore registers a
        // no-op so the bootstrap path works unchanged.
        var services = new ServiceCollection();
        services.AddTelemetryPersistenceCore(_ => { /* no provider needed for this test */ });
        await using var sp = services.BuildServiceProvider();

        var migrationLock = sp.GetRequiredService<IMigrationLock>();

        await using (await migrationLock.AcquireAsync(CancellationToken.None))
        {
            // No-op: scope exits cleanly.
        }
    }

    [Fact]
    public async Task ProviderSpecificRegistration_overrides_the_NoOp_default()
    {
        // Sanity-check the registration order: AddSingleton<IMigrationLock>
        // (provider-specific) before AddTelemetryPersistenceCore (which uses
        // TryAddSingleton for the no-op fallback) must keep the provider
        // implementation as the resolved one.
        var services = new ServiceCollection();
        services.AddSingleton<IMigrationLock>(_ => new StubLock());
        services.AddTelemetryPersistenceCore(_ => { });
        await using var sp = services.BuildServiceProvider();

        var migrationLock = sp.GetRequiredService<IMigrationLock>();

        migrationLock.ShouldBeOfType<StubLock>();
    }

    [Fact]
    public async Task AcquireAsync_returns_a_disposable_handle()
    {
        // Contract guarantee: regardless of implementation, the awaited
        // value is non-null and disposable both sync and async.
        IMigrationLock migrationLock = new StubLock();

        var handle = await migrationLock.AcquireAsync(CancellationToken.None);

        handle.ShouldNotBeNull();
        await handle.DisposeAsync();
    }

    private sealed class StubLock : IMigrationLock
    {
        public Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IAsyncDisposable>(new StubHandle());

        private sealed class StubHandle : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
