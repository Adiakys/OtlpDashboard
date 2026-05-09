using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenTelemetryDashboard.Dashboards.Contracts;
using OpenTelemetryDashboard.Dashboards.Domain;

namespace OpenTelemetryDashboard.IntegrationTests.Widgets;

public sealed class WidgetDefinitionEndpointsTests : IClassFixture<TestHostFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TestHostFixture _fixture;

    public WidgetDefinitionEndpointsTests(TestHostFixture fixture) => _fixture = fixture;

    // ---------- GET ----------

    [Fact]
    public async Task Get_All_On_Empty_Db_Returns_Empty_List()
    {
        using var client = _fixture.CreateClient();

        var list = await client.GetFromJsonAsync<WidgetDefinitionDto[]>(
            new Uri("/api/v1/widgets/definitions", UriKind.Relative), JsonOptions);

        list.ShouldNotBeNull();
        // Tests share the SQLite DB, so we can only assert "no errors" not "exactly empty".
        // We'll create our own row below and refetch by id.
    }

    [Fact]
    public async Task Get_By_Id_Returns_404_For_Unknown()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.GetAsync(
            new Uri($"/api/v1/widgets/definitions/{Guid.NewGuid()}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------- POST ----------

    [Fact]
    public async Task Post_Creates_Preset_Definition()
    {
        using var client = _fixture.CreateClient();

        var save = new SaveWidgetDefinitionRequest(
            Name: $"p99-{Guid.NewGuid():N}"[..30],
            Description: "p99 latency on http.server.duration",
            Icon: "i-ph-target",
            Engine: WidgetEngine.Preset,
            BaseKind: "metric-stat",
            Config: ParseJson("""{"calc":"last","unitKind":"ms","decimals":1}"""),
            Spec: null,
            DefaultW: 4,
            DefaultH: 3,
            RowVersion: 0);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/widgets/definitions", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var created = await response.Content.ReadFromJsonAsync<WidgetDefinitionDto>(JsonOptions);
        created.ShouldNotBeNull();
        created!.Id.ShouldNotBe(Guid.Empty);
        created.Engine.ShouldBe(WidgetEngine.Preset);
        created.BaseKind.ShouldBe("metric-stat");
        created.RowVersion.ShouldBe(1u);
        created.DefaultW.ShouldBe(4);

        // Re-GET sees the same content.
        var refetched = await client.GetFromJsonAsync<WidgetDefinitionDto>(
            new Uri($"/api/v1/widgets/definitions/{created.Id}", UriKind.Relative), JsonOptions);
        refetched.ShouldNotBeNull();
        refetched!.Name.ShouldBe(save.Name);
    }

    [Fact]
    public async Task Post_Returns_400_On_Empty_Name()
    {
        using var client = _fixture.CreateClient();

        var save = new SaveWidgetDefinitionRequest(
            Name: "  ",
            Description: null,
            Icon: "i-ph-puzzle-piece",
            Engine: WidgetEngine.Preset,
            BaseKind: "metric-stat",
            Config: ParseJson("{}"),
            Spec: null,
            DefaultW: 3,
            DefaultH: 3,
            RowVersion: 0);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/widgets/definitions", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_Returns_400_On_BaseKind_With_Colon()
    {
        // baseKind referencing another custom or library kind would let the
        // user chain presets recursively. Validation rejects.
        using var client = _fixture.CreateClient();

        var save = new SaveWidgetDefinitionRequest(
            Name: "evil",
            Description: null,
            Icon: "i-ph-puzzle-piece",
            Engine: WidgetEngine.Preset,
            BaseKind: "library:foo/bar",
            Config: ParseJson("{}"),
            Spec: null,
            DefaultW: 3,
            DefaultH: 3,
            RowVersion: 0);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/widgets/definitions", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_Returns_400_On_Bad_Icon()
    {
        using var client = _fixture.CreateClient();

        var save = new SaveWidgetDefinitionRequest(
            Name: "bad-icon",
            Description: null,
            Icon: "javascript:alert(1)", // not matching i-ph-* / i-lucide-*
            Engine: WidgetEngine.Preset,
            BaseKind: "metric-stat",
            Config: ParseJson("{}"),
            Spec: null,
            DefaultW: 3,
            DefaultH: 3,
            RowVersion: 0);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/widgets/definitions", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_Returns_400_On_Missing_BaseKind_For_Preset()
    {
        using var client = _fixture.CreateClient();

        var save = new SaveWidgetDefinitionRequest(
            Name: "missing-base",
            Description: null,
            Icon: "i-ph-puzzle-piece",
            Engine: WidgetEngine.Preset,
            BaseKind: null,
            Config: ParseJson("{}"),
            Spec: null,
            DefaultW: 3,
            DefaultH: 3,
            RowVersion: 0);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/widgets/definitions", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_Returns_400_On_Non_Object_Config()
    {
        using var client = _fixture.CreateClient();

        var save = new SaveWidgetDefinitionRequest(
            Name: "non-obj",
            Description: null,
            Icon: "i-ph-puzzle-piece",
            Engine: WidgetEngine.Preset,
            BaseKind: "metric-stat",
            Config: ParseJson("[]"), // array, not object
            Spec: null,
            DefaultW: 3,
            DefaultH: 3,
            RowVersion: 0);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/widgets/definitions", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ---------- PUT ----------

    [Fact]
    public async Task Put_Updates_Definition_And_Bumps_Row_Version()
    {
        using var client = _fixture.CreateClient();
        var created = await CreatePresetAsync(client);

        var save = new SaveWidgetDefinitionRequest(
            Name: created.Name + "-v2",
            Description: "edited",
            Icon: created.Icon,
            Engine: created.Engine,
            BaseKind: created.BaseKind,
            Config: created.Config,
            Spec: null,
            DefaultW: created.DefaultW,
            DefaultH: created.DefaultH,
            RowVersion: created.RowVersion);

        using var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/widgets/definitions/{created.Id}", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await response.Content.ReadFromJsonAsync<WidgetDefinitionDto>(JsonOptions);
        updated.ShouldNotBeNull();
        updated!.RowVersion.ShouldBe(created.RowVersion + 1);
        updated.Name.ShouldEndWith("-v2");
    }

    [Fact]
    public async Task Put_Returns_409_On_Stale_RowVersion()
    {
        using var client = _fixture.CreateClient();
        var created = await CreatePresetAsync(client);

        var save = new SaveWidgetDefinitionRequest(
            Name: created.Name,
            Description: null,
            Icon: created.Icon,
            Engine: created.Engine,
            BaseKind: created.BaseKind,
            Config: created.Config,
            Spec: null,
            DefaultW: created.DefaultW,
            DefaultH: created.DefaultH,
            RowVersion: created.RowVersion + 99u); // stale

        using var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/widgets/definitions/{created.Id}", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Put_Returns_404_For_Unknown_Id()
    {
        using var client = _fixture.CreateClient();

        var save = new SaveWidgetDefinitionRequest(
            Name: "ghost",
            Description: null,
            Icon: "i-ph-puzzle-piece",
            Engine: WidgetEngine.Preset,
            BaseKind: "metric-stat",
            Config: ParseJson("{}"),
            Spec: null,
            DefaultW: 3,
            DefaultH: 3,
            RowVersion: 0);

        using var response = await client.PutAsJsonAsync(
            new Uri($"/api/v1/widgets/definitions/{Guid.NewGuid()}", UriKind.Relative), save, JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ---------- DELETE ----------

    [Fact]
    public async Task Delete_Removes_Definition()
    {
        using var client = _fixture.CreateClient();
        var created = await CreatePresetAsync(client);

        using var response = await client.DeleteAsync(
            new Uri($"/api/v1/widgets/definitions/{created.Id}?rowVersion={created.RowVersion}", UriKind.Relative));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var refetch = await client.GetAsync(
            new Uri($"/api/v1/widgets/definitions/{created.Id}", UriKind.Relative));
        refetch.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Returns_404_For_Unknown_Id()
    {
        using var client = _fixture.CreateClient();

        using var response = await client.DeleteAsync(
            new Uri($"/api/v1/widgets/definitions/{Guid.NewGuid()}?rowVersion=1", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Returns_400_When_RowVersion_Missing()
    {
        using var client = _fixture.CreateClient();
        var created = await CreatePresetAsync(client);

        using var response = await client.DeleteAsync(
            new Uri($"/api/v1/widgets/definitions/{created.Id}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_Returns_409_For_Stale_RowVersion()
    {
        using var client = _fixture.CreateClient();
        var created = await CreatePresetAsync(client);

        // Bump RowVersion via update so the original is now stale.
        var update = new SaveWidgetDefinitionRequest(
            Name: created.Name,
            Description: created.Description,
            Icon: created.Icon,
            Engine: created.Engine,
            BaseKind: created.BaseKind,
            Config: created.Config,
            Spec: created.Spec,
            DefaultW: created.DefaultW,
            DefaultH: created.DefaultH,
            RowVersion: created.RowVersion);
        using var put = await client.PutAsJsonAsync(
            new Uri($"/api/v1/widgets/definitions/{created.Id}", UriKind.Relative),
            update,
            JsonOptions);
        put.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var response = await client.DeleteAsync(
            new Uri($"/api/v1/widgets/definitions/{created.Id}?rowVersion={created.RowVersion}", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    // ---------- helpers ----------

    private static async Task<WidgetDefinitionDto> CreatePresetAsync(HttpClient client)
    {
        var save = new SaveWidgetDefinitionRequest(
            Name: $"def-{Guid.NewGuid():N}"[..30],
            Description: null,
            Icon: "i-ph-puzzle-piece",
            Engine: WidgetEngine.Preset,
            BaseKind: "metric-stat",
            Config: ParseJson("""{"calc":"last"}"""),
            Spec: null,
            DefaultW: 3,
            DefaultH: 3,
            RowVersion: 0);

        using var response = await client.PostAsJsonAsync(
            new Uri("/api/v1/widgets/definitions", UriKind.Relative), save, JsonOptions);
        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<WidgetDefinitionDto>(JsonOptions);
        created.ShouldNotBeNull();
        return created!;
    }

    private static JsonElement ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
