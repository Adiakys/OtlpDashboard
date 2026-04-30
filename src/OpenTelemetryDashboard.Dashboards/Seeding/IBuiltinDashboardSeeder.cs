namespace OpenTelemetryDashboard.Dashboards.Seeding;

/// <summary>
/// One-shot seeding port. Invoked by the host once after EF Core
/// migrations have run and before traffic is served. Implementations
/// scan <see cref="DashboardsOptions.BuiltinPaths"/> for JSON dashboard
/// files, validate them, and fold each into <see cref="Storage.IDashboardStore"/>
/// with strict idempotency: an id already present in the store is
/// skipped silently.
/// </summary>
public interface IBuiltinDashboardSeeder
{
    Task SeedAsync(CancellationToken cancellationToken);
}
