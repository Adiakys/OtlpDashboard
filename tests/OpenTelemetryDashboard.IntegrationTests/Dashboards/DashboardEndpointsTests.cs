using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenTelemetryDashboard.Dashboards.Contracts;
using OpenTelemetryDashboard.Dashboards.Domain;

namespace OpenTelemetryDashboard.IntegrationTests.Dashboards;

public sealed class DashboardEndpointsTests : IClassFixture<TestHostFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TestHostFixture _fixture;

    public DashboardEndpointsTests(TestHostFixture fixture) => _fixture = fixture;

    // ---------- GET ----------

    [Fact]
    public async Task Get_All_Includes_Seeded_Default_Dashboard()
    {
        using var client = _fixture.CreateClient();

        var list = await client.GetFromJsonAsync<DashboardDto[]>(
            new Uri("/api/v1/dashboards", UriKind.Relative), JsonOptions);

        list.ShouldNotBeNull();
        list!.ShouldContain(d => d.Id == Dashboard.DefaultId);
    }

    [Fact]
    public async Task Get_By_Id_Returns_Default_Seed()
    {
        using var client = _fixture.CreateClient();

        var dto = await client.GetFromJsonAsync<DashboardDto>(
            new Uri($"/api/v1/dashboards/{Dashboard.DefaultId}", UriKind.Relative), JsonOptions);

        dto.ShouldNotBeNull();
        dto!.Id.ShouldBe(Dashboard.DefaultId);
        dto.Widgets.ShouldNotBeNull();
    }

    [Fact]
    public async Task Get_By_Id_Returns_404_For_Unknown()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.GetAsync(
            new Uri($"/api/v1/dashboards/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------- POST ----------

    [Fact]
    public async Task Post_Creates_New_Dashboard_With_Widgets()
    {
        using var client = _fixture.CreateClient();
        var name = $"d-{Guid.NewGuid():N}"[..30];

        var save = new SaveDashboardRequest(
            Name: name,
            Widgets:
            [
                new DashboardWidgetDto(
                    Id: Guid.Empty,
                    Kind: "text",
                    X: 0, Y: 0, W: 4, H: 2,
                    Config: ParseConfig("""{"markdown":"hello"}"""))
            ],
            RowVersion: 0);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/dashboards", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await response.Content.ReadFromJsonAsync<DashboardDto>(JsonOptions);
        created.ShouldNotBeNull();
        created!.Name.ShouldBe(name);
        created.Widgets.Count.ShouldBe(1);
        created.Widgets[0].Kind.ShouldBe("text");
        // Server generates the widget id when client passes Guid.Empty.
        created.Widgets[0].Id.ShouldNotBe(Guid.Empty);
        created.RowVersion.ShouldBe(1u);

        // Re-GET sees the same content.
        var refetched = await client.GetFromJsonAsync<DashboardDto>(
            new Uri($"/api/v1/dashboards/{created.Id}", UriKind.Relative), JsonOptions);
        refetched.ShouldNotBeNull();
        refetched!.Widgets.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Post_Returns_400_On_Empty_Name()
    {
        using var client = _fixture.CreateClient();

        var save = new SaveDashboardRequest(
            Name: "  ",
            Widgets: [],
            RowVersion: 0);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/dashboards", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_Returns_400_On_Widget_With_Non_Object_Config()
    {
        using var client = _fixture.CreateClient();

        var save = new SaveDashboardRequest(
            Name: "ok",
            Widgets:
            [
                new DashboardWidgetDto(
                    Id: Guid.Empty, Kind: "text",
                    X: 0, Y: 0, W: 1, H: 1,
                    Config: ParseConfig("\"plain string\""))
            ],
            RowVersion: 0);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/dashboards", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_Returns_400_On_Oversized_Widget_Config()
    {
        using var client = _fixture.CreateClient();

        // Build a config object whose serialized form exceeds the per-widget cap.
        var giant = new string('a', SaveDashboardRequest.MaxConfigBytes + 1);
        var save = new SaveDashboardRequest(
            Name: "big",
            Widgets:
            [
                new DashboardWidgetDto(
                    Id: Guid.Empty, Kind: "text",
                    X: 0, Y: 0, W: 1, H: 1,
                    Config: JsonSerializer.SerializeToElement(new { markdown = giant }))
            ],
            RowVersion: 0);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/dashboards", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------- PUT ----------

    [Fact]
    public async Task Put_Updates_Dashboard_And_Bumps_RowVersion()
    {
        using var client = _fixture.CreateClient();
        var created = await CreateDashboardAsync(client, $"ph-{Guid.NewGuid():N}"[..30]);

        var save = new SaveDashboardRequest(
            Name: created.Name,
            Widgets:
            [
                new DashboardWidgetDto(
                    Id: Guid.Empty, Kind: "text",
                    X: 1, Y: 1, W: 3, H: 2,
                    Config: ParseConfig("""{"markdown":"updated"}"""))
            ],
            RowVersion: created.RowVersion);

        using var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/dashboards/{created.Id}", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<DashboardDto>(JsonOptions);
        updated.ShouldNotBeNull();
        updated!.RowVersion.ShouldBe(created.RowVersion + 1);
        updated.Widgets.Count.ShouldBe(1);
        updated.Widgets[0].X.ShouldBe(1);
    }

    [Fact]
    public async Task Put_Returns_404_On_Unknown_Id()
    {
        using var client = _fixture.CreateClient();

        var save = new SaveDashboardRequest(
            Name: "ghost", Widgets: [], RowVersion: 0);

        using var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/dashboards/{Guid.NewGuid()}", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_Returns_409_On_Stale_RowVersion()
    {
        using var client = _fixture.CreateClient();
        var created = await CreateDashboardAsync(client, $"ps-{Guid.NewGuid():N}"[..30]);

        var stale = new SaveDashboardRequest(
            Name: created.Name,
            Widgets: [],
            RowVersion: created.RowVersion + 999);

        using var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/dashboards/{created.Id}", UriKind.Relative), stale, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Put_Returns_400_On_Empty_Name()
    {
        using var client = _fixture.CreateClient();
        var created = await CreateDashboardAsync(client, $"pe-{Guid.NewGuid():N}"[..30]);

        var save = new SaveDashboardRequest(
            Name: "", Widgets: [], RowVersion: created.RowVersion);

        using var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/dashboards/{created.Id}", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_Reconciles_Widgets_Add_Update_Delete()
    {
        using var client = _fixture.CreateClient();
        var created = await CreateDashboardAsync(
            client,
            $"pr-{Guid.NewGuid():N}"[..30],
            new DashboardWidgetDto(Guid.Empty, "text", 0, 0, 2, 2, ParseConfig("""{"markdown":"a"}""")),
            new DashboardWidgetDto(Guid.Empty, "text", 0, 2, 2, 2, ParseConfig("""{"markdown":"b"}""")));

        // Carry over the first widget (mutated), drop the second, add a new one.
        var keep = created.Widgets[0]!;
        var save = new SaveDashboardRequest(
            Name: created.Name,
            Widgets:
            [
                new DashboardWidgetDto(keep.Id, keep.Kind, keep.X, keep.Y, keep.W, keep.H,
                    ParseConfig("""{"markdown":"a-updated"}""")),
                new DashboardWidgetDto(Guid.Empty, "text", 4, 0, 2, 2, ParseConfig("""{"markdown":"c"}""")),
            ],
            RowVersion: created.RowVersion);

        using var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/dashboards/{created.Id}", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var refetched = await client.GetFromJsonAsync<DashboardDto>(
            new Uri($"/api/v1/dashboards/{created.Id}", UriKind.Relative), JsonOptions);

        refetched.ShouldNotBeNull();
        refetched!.Widgets.Count.ShouldBe(2);
        refetched.Widgets.ShouldContain(w => w.Id == keep.Id);
        // Stale widget is gone.
        refetched.Widgets.ShouldNotContain(w => w.Id == created.Widgets[1]!.Id);
        // Updated config landed on the kept widget.
        refetched.Widgets
            .First(w => w.Id == keep.Id).Config
            .GetProperty("markdown").GetString().ShouldBe("a-updated");
    }

    // ---------- DELETE ----------

    [Fact]
    public async Task Delete_Removes_Dashboard()
    {
        using var client = _fixture.CreateClient();
        var created = await CreateDashboardAsync(client, $"dl-{Guid.NewGuid():N}"[..30]);

        using var response = await client.DeleteAsync(
            new Uri($"/api/v1/dashboards/{created.Id}?rowVersion={created.RowVersion}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var refetch = await client.GetAsync(
            new Uri($"/api/v1/dashboards/{created.Id}", UriKind.Relative));
        refetch.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Returns_400_For_Default_Dashboard()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.DeleteAsync(
            new Uri($"/api/v1/dashboards/{Dashboard.DefaultId}?rowVersion=1", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_Returns_404_For_Unknown_Id()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.DeleteAsync(
            new Uri($"/api/v1/dashboards/{Guid.NewGuid()}?rowVersion=1", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Returns_400_When_RowVersion_Missing()
    {
        // The query param is non-nullable on the endpoint signature, so a
        // request without it fails model binding at the framework layer.
        using var client = _fixture.CreateClient();
        var created = await CreateDashboardAsync(client, $"dl-{Guid.NewGuid():N}"[..30]);

        using var response = await client.DeleteAsync(
            new Uri($"/api/v1/dashboards/{created.Id}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_Returns_409_For_Stale_RowVersion()
    {
        // Update bumps RowVersion to 2; a delete carrying RowVersion=1 must
        // be refused as concurrency conflict, not silently dropped.
        using var client = _fixture.CreateClient();
        var created = await CreateDashboardAsync(client, $"dl-{Guid.NewGuid():N}"[..30]);

        using var update = await client.PutAsJsonAsync(
            new Uri($"/api/v1/dashboards/{created.Id}", UriKind.Relative),
            new SaveDashboardRequest(created.Name, [], created.RowVersion),
            JsonOptions);
        update.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var response = await client.DeleteAsync(
            new Uri($"/api/v1/dashboards/{created.Id}?rowVersion={created.RowVersion}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ---------- helpers ----------

    private static async Task<DashboardDto> CreateDashboardAsync(
        HttpClient client,
        string name,
        params DashboardWidgetDto[] widgets)
    {
        var save = new SaveDashboardRequest(name, widgets, RowVersion: 0);
        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/dashboards", UriKind.Relative), save, JsonOptions);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<DashboardDto>(JsonOptions);
        created.ShouldNotBeNull();
        return created!;
    }

    private static JsonElement ParseConfig(string json)
    {
        // JsonDocument.Parse owns its buffer — clone so the element survives
        // the using scope.
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
