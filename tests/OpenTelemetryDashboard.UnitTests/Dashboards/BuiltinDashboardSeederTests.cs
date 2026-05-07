using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Library;
using OpenTelemetryDashboard.Dashboards.Seeding;
using OpenTelemetryDashboard.Dashboards.Storage;

namespace OpenTelemetryDashboard.UnitTests.Dashboards;

public sealed class BuiltinDashboardSeederTests
{
    [Fact]
    public async Task Seeds_Builtin_Dashboard_With_Explicit_Id()
    {
        var pack = MakePack("team", [
            new PackDashboard
            {
                Id = "team-overview",
                RawJson = """
                    {"version":1,"id":"11111111-1111-1111-1111-111111111111","name":"Team","widgets":[]}
                    """,
                Builtin = true
            }
        ]);

        var (seeder, store) = NewSeeder([pack]);
        await seeder.SeedAsync(CancellationToken.None);

        store.Added.Count.ShouldBe(2); // pack-supplied + empty default
        store.Added.ShouldContain(d => d.Id == new Guid("11111111-1111-1111-1111-111111111111") && d.Name == "Team");
        store.Added.ShouldContain(d => d.Id == Dashboard.DefaultId);
    }

    [Fact]
    public async Task Skips_Non_Builtin_Dashboards()
    {
        var pack = MakePack("team", [
            new PackDashboard
            {
                Id = "ops",
                RawJson = """
                    {"version":1,"id":"22222222-2222-2222-2222-222222222222","name":"Ops","widgets":[]}
                    """,
                Builtin = false
            }
        ]);

        var (seeder, store) = NewSeeder([pack]);
        await seeder.SeedAsync(CancellationToken.None);

        store.Added.ShouldNotContain(d => d.Id == new Guid("22222222-2222-2222-2222-222222222222"));
        // Empty default still seeded.
        store.Added.Count.ShouldBe(1);
        store.Added[0].Id.ShouldBe(Dashboard.DefaultId);
    }

    [Fact]
    public async Task Skips_Silently_When_Id_Already_Present()
    {
        var pack = MakePack("team", [
            new PackDashboard
            {
                Id = "team",
                RawJson = """
                    {"version":1,"id":"11111111-1111-1111-1111-111111111111","name":"Team","widgets":[]}
                    """,
                Builtin = true
            }
        ]);

        var (seeder, store) = NewSeeder([pack]);
        store.SeedExisting(new Guid("11111111-1111-1111-1111-111111111111"));
        store.SeedExisting(Dashboard.DefaultId);

        await seeder.SeedAsync(CancellationToken.None);

        store.Added.ShouldBeEmpty();
    }

    [Fact]
    public async Task Default_Id_Maps_From_DashId_Equals_Default()
    {
        var pack = MakePack("team", [
            new PackDashboard
            {
                Id = "default",
                RawJson = """{"version":1,"name":"Welcome","widgets":[]}""",
                Builtin = true
            }
        ]);

        var (seeder, store) = NewSeeder([pack]);
        await seeder.SeedAsync(CancellationToken.None);

        store.Added.Count.ShouldBe(1);
        store.Added[0].Id.ShouldBe(Dashboard.DefaultId);
        store.Added[0].Name.ShouldBe("Welcome");
    }

    [Fact]
    public async Task DashId_Without_Explicit_Guid_Generates_Deterministic_Id()
    {
        var pack = MakePack("team", [
            new PackDashboard
            {
                Id = "alpha",
                RawJson = """{"version":1,"name":"Alpha","widgets":[]}""",
                Builtin = true
            }
        ]);

        var (seeder1, store1) = NewSeeder([pack]);
        await seeder1.SeedAsync(CancellationToken.None);
        var first = store1.Added.Single(d => d.Name == "Alpha").Id;

        // Re-seed into a fresh store: same packId/dashId must yield the
        // same Guid, otherwise idempotency breaks.
        var (seeder2, store2) = NewSeeder([pack]);
        await seeder2.SeedAsync(CancellationToken.None);
        var second = store2.Added.Single(d => d.Name == "Alpha").Id;

        first.ShouldBe(second);
    }

    [Fact]
    public async Task Empty_Default_Created_When_No_Pack_Ships_One()
    {
        var (seeder, store) = NewSeeder([]);
        await seeder.SeedAsync(CancellationToken.None);

        store.Added.Count.ShouldBe(1);
        store.Added[0].Id.ShouldBe(Dashboard.DefaultId);
        store.Added[0].Name.ShouldBe("main");
        store.Added[0].Widgets.ShouldBeEmpty();
    }

    [Fact]
    public async Task Default_File_Upserts_Pristine_Default()
    {
        var pack = MakePack("team", [
            new PackDashboard
            {
                Id = "default",
                RawJson = """{"version":1,"name":"Welcome","widgets":[]}""",
                Builtin = true
            }
        ]);

        var (seeder, store) = NewSeeder([pack]);
        store.SeedExisting(new Dashboard
        {
            Id = Dashboard.DefaultId,
            Name = "main",
            UpdatedAt = DateTimeOffset.MinValue,
            RowVersion = 0,
            Widgets = []
        });

        await seeder.SeedAsync(CancellationToken.None);

        store.Updated.Count.ShouldBe(1);
        store.Updated[0].Id.ShouldBe(Dashboard.DefaultId);
        store.Updated[0].Name.ShouldBe("Welcome");
        store.Added.ShouldBeEmpty();
    }

    [Fact]
    public async Task Default_File_Skipped_If_User_Modified()
    {
        var pack = MakePack("team", [
            new PackDashboard
            {
                Id = "default",
                RawJson = """{"version":1,"name":"Welcome","widgets":[]}""",
                Builtin = true
            }
        ]);

        var (seeder, store) = NewSeeder([pack]);
        store.SeedExisting(new Dashboard
        {
            Id = Dashboard.DefaultId,
            Name = "main",
            UpdatedAt = DateTimeOffset.UtcNow,
            RowVersion = 1, // user has saved at least once
            Widgets = []
        });

        await seeder.SeedAsync(CancellationToken.None);

        store.Updated.ShouldBeEmpty();
        store.Added.ShouldBeEmpty();
    }

    [Fact]
    public async Task First_Pack_Wins_On_Id_Collision()
    {
        var primary = MakePack("primary", [
            new PackDashboard
            {
                Id = "shared",
                RawJson = """
                    {"version":1,"id":"44444444-4444-4444-4444-444444444444","name":"Primary wins","widgets":[]}
                    """,
                Builtin = true
            }
        ]);
        var secondary = MakePack("secondary", [
            new PackDashboard
            {
                Id = "shared",
                RawJson = """
                    {"version":1,"id":"44444444-4444-4444-4444-444444444444","name":"Secondary loses","widgets":[]}
                    """,
                Builtin = true
            }
        ]);

        var (seeder, store) = NewSeeder([primary, secondary]);
        await seeder.SeedAsync(CancellationToken.None);

        var added = store.Added.Single(d => d.Id == new Guid("44444444-4444-4444-4444-444444444444"));
        added.Name.ShouldBe("Primary wins");
    }

    [Fact]
    public async Task Invalid_Dashboard_Is_Skipped_Without_Killing_Others()
    {
        var pack = MakePack("team", [
            new PackDashboard
            {
                Id = "good",
                RawJson = """
                    {"version":1,"id":"55555555-5555-5555-5555-555555555555","name":"Good","widgets":[]}
                    """,
                Builtin = true
            },
            new PackDashboard
            {
                Id = "bad",
                RawJson = "{ totally not valid }",
                Builtin = true
            }
        ]);

        var (seeder, store) = NewSeeder([pack]);
        await seeder.SeedAsync(CancellationToken.None);

        store.Added.ShouldContain(d => d.Id == new Guid("55555555-5555-5555-5555-555555555555"));
    }

    // --------------------------------------------------------------

    private static (BuiltinDashboardSeeder Seeder, FakeDashboardStore Store) NewSeeder(IReadOnlyList<Pack> packs)
    {
        var store = new FakeDashboardStore();
        var registry = new FakePackRegistry(packs);
        var seeder = new BuiltinDashboardSeeder(store, registry, NullLogger<BuiltinDashboardSeeder>.Instance);
        return (seeder, store);
    }

    private static Pack MakePack(string id, IReadOnlyList<PackDashboard> dashboards) =>
        new()
        {
            Id = id,
            Name = id,
            Version = "1.0.0",
            Dashboards = dashboards,
            Libraries = []
        };

    private sealed class FakePackRegistry(IReadOnlyList<Pack> packs) : IPackRegistry
    {
        public Task<IReadOnlyList<Pack>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult(packs);

        public Task ReloadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task UninstallAsync(string packId, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }

    private sealed class FakeDashboardStore : IDashboardStore
    {
        public List<Dashboard> Added { get; } = [];
        public List<Dashboard> Updated { get; } = [];
        private readonly Dictionary<Guid, Dashboard> _existing = [];

        public void SeedExisting(Dashboard dashboard) => _existing[dashboard.Id] = dashboard;
        public void SeedExisting(Guid id) => _existing[id] = new Dashboard { Id = id, Name = "x" };

        public Task<IReadOnlyList<Dashboard>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Dashboard>>(_existing.Values.ToList());

        public Task<IReadOnlyList<Guid>> GetAllIdsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Guid>>(_existing.Keys.ToList());

        public Task<Dashboard?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_existing.TryGetValue(id, out var d) ? d : null);

        public Task AddAsync(Dashboard dashboard, CancellationToken cancellationToken = default)
        {
            Added.Add(dashboard);
            _existing[dashboard.Id] = dashboard;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Dashboard dashboard, CancellationToken cancellationToken = default)
        {
            Updated.Add(dashboard);
            _existing[dashboard.Id] = dashboard;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Dashboard dashboard, CancellationToken cancellationToken = default)
        {
            _existing.Remove(dashboard.Id);
            return Task.CompletedTask;
        }
    }
}
