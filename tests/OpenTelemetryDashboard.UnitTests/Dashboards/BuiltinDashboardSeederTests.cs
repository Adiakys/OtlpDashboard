using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Dashboards;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Seeding;
using OpenTelemetryDashboard.Dashboards.Storage;

namespace OpenTelemetryDashboard.UnitTests.Dashboards;

public sealed class BuiltinDashboardSeederTests : IDisposable
{
    private readonly string _root;

    public BuiltinDashboardSeederTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"otel-seed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Seeds_File_Without_Existing_Id()
    {
        WriteFile("team.json", new SeedJson { Name = "Team", Id = "11111111-1111-1111-1111-111111111111" });

        var (seeder, store) = NewSeeder();
        await seeder.SeedAsync(CancellationToken.None);

        store.Added.Count.ShouldBe(2); // file + empty default
        store.Added.ShouldContain(d => d.Id == new Guid("11111111-1111-1111-1111-111111111111") && d.Name == "Team");
        store.Added.ShouldContain(d => d.Id == Dashboard.DefaultId);
    }

    [Fact]
    public async Task Skips_Silently_When_Id_Already_Present()
    {
        WriteFile("team.json", new SeedJson { Name = "Team", Id = "11111111-1111-1111-1111-111111111111" });

        var (seeder, store) = NewSeeder();
        store.SeedExisting(new Guid("11111111-1111-1111-1111-111111111111"));
        store.SeedExisting(Dashboard.DefaultId);

        await seeder.SeedAsync(CancellationToken.None);

        store.Added.ShouldBeEmpty();
    }

    [Fact]
    public async Task Default_File_Maps_To_DefaultId()
    {
        WriteFile("default.json", new SeedJson { Name = "Welcome" });

        var (seeder, store) = NewSeeder();
        await seeder.SeedAsync(CancellationToken.None);

        store.Added.Count.ShouldBe(1);
        store.Added[0].Id.ShouldBe(Dashboard.DefaultId);
        store.Added[0].Name.ShouldBe("Welcome");
    }

    [Fact]
    public async Task Filename_Without_Id_Generates_Deterministic_Id()
    {
        WriteFile("alpha.json", new SeedJson { Name = "Alpha" });

        var (seeder, store) = NewSeeder();
        await seeder.SeedAsync(CancellationToken.None);

        var first = store.Added.Single(d => d.Name == "Alpha").Id;

        // Run again into a fresh store: the same filename must yield the
        // same Guid, otherwise idempotency breaks.
        var (seeder2, store2) = NewSeeder();
        await seeder2.SeedAsync(CancellationToken.None);
        var second = store2.Added.Single(d => d.Name == "Alpha").Id;

        first.ShouldBe(second);
    }

    [Fact]
    public async Task Empty_Default_Created_When_No_Default_File()
    {
        var (seeder, store) = NewSeeder();
        await seeder.SeedAsync(CancellationToken.None);

        store.Added.Count.ShouldBe(1);
        store.Added[0].Id.ShouldBe(Dashboard.DefaultId);
        store.Added[0].Name.ShouldBe("main");
        store.Added[0].Widgets.ShouldBeEmpty();
    }

    [Fact]
    public async Task Default_File_Upserts_Pristine_Default()
    {
        WriteFile("default.json", new SeedJson { Name = "Welcome" });

        var (seeder, store) = NewSeeder();
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
        WriteFile("default.json", new SeedJson { Name = "Welcome" });

        var (seeder, store) = NewSeeder();
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
    public async Task First_Path_Wins_On_Id_Collision()
    {
        var primary = Path.Combine(_root, "primary");
        var secondary = Path.Combine(_root, "secondary");
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(secondary);

        WriteFileAt(primary, "shared.json", new SeedJson { Id = "44444444-4444-4444-4444-444444444444", Name = "Primary wins" });
        WriteFileAt(secondary, "shared.json", new SeedJson { Id = "44444444-4444-4444-4444-444444444444", Name = "Secondary loses" });

        var (seeder, store) = NewSeeder([primary, secondary]);
        await seeder.SeedAsync(CancellationToken.None);

        var added = store.Added.Single(d => d.Id == new Guid("44444444-4444-4444-4444-444444444444"));
        added.Name.ShouldBe("Primary wins");
    }

    [Fact]
    public async Task Invalid_File_Is_Skipped_Without_Killing_Others()
    {
        WriteFile("good.json", new SeedJson { Name = "Good", Id = "55555555-5555-5555-5555-555555555555" });
        File.WriteAllText(Path.Combine(_root, "bad.json"), "{ totally not valid }");

        var (seeder, store) = NewSeeder();
        await seeder.SeedAsync(CancellationToken.None);

        store.Added.ShouldContain(d => d.Id == new Guid("55555555-5555-5555-5555-555555555555"));
    }

    private (BuiltinDashboardSeeder Seeder, FakeDashboardStore Store) NewSeeder(IEnumerable<string>? paths = null)
    {
        var opts = Options.Create(new DashboardsOptions
        {
            BuiltinPaths = paths?.ToList() ?? [_root]
        });
        var store = new FakeDashboardStore();
        var seeder = new BuiltinDashboardSeeder(store, opts, NullLogger<BuiltinDashboardSeeder>.Instance);
        return (seeder, store);
    }

    private void WriteFile(string filename, SeedJson contents) => WriteFileAt(_root, filename, contents);

    private static void WriteFileAt(string dir, string filename, SeedJson contents)
    {
        var idLine = contents.Id is null ? string.Empty : $"\"id\": \"{contents.Id}\",";
        var json = $$"""
        {
          "version": 1,
          {{idLine}}
          "name": "{{contents.Name}}",
          "widgets": []
        }
        """;
        File.WriteAllText(Path.Combine(dir, filename), json);
    }

    private sealed class SeedJson
    {
        public string? Id { get; init; }
        public required string Name { get; init; }
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
