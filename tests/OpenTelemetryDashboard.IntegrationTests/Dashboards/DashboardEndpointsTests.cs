using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using OpenTelemetryDashboard.Dashboards.Contracts;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Validation;

namespace OpenTelemetryDashboard.IntegrationTests.Dashboards;

public sealed class DashboardEndpointsTests : IClassFixture<TestHostFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TestHostFixture _fixture;

    public DashboardEndpointsTests(TestHostFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Get_Default_Lazy_Creates_Empty_Layout()
    {
        using var client = _fixture.CreateClient();

        var dto = await client.GetFromJsonAsync<DashboardDto>(
            new Uri("/api/v1/dashboards/default", UriKind.Relative),
            JsonOptions);

        dto.ShouldNotBeNull();
        dto.Id.ShouldBe(Dashboard.DefaultId);
        dto.Name.ShouldBe("Default");
        dto.LayoutJson.ShouldBe("""{"widgets":[]}""");
        dto.RowVersion.ShouldBeGreaterThan(0u);
    }

    [Fact]
    public async Task Put_Default_Persists_And_Bumps_RowVersion()
    {
        using var client = _fixture.CreateClient();

        var current = await client.GetFromJsonAsync<DashboardDto>(
            new Uri("/api/v1/dashboards/default", UriKind.Relative),
            JsonOptions);
        current.ShouldNotBeNull();

        var save = new SaveDashboardRequest(
            Name: "Test",
            LayoutJson: """{"widgets":[{"id":"a","kind":"text","x":0,"y":0,"w":2,"h":1,"config":{"markdown":"hi"}}]}""",
            RowVersion: current.RowVersion);

        using var putResponse = await client.PutAsJsonAsync(
            new Uri("/api/v1/dashboards/default", UriKind.Relative),
            save,
            JsonOptions);

        putResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = await putResponse.Content.ReadFromJsonAsync<DashboardDto>(JsonOptions);
        saved.ShouldNotBeNull();
        saved.Name.ShouldBe("Test");
        saved.LayoutJson.ShouldBe(save.LayoutJson);
        saved.RowVersion.ShouldBe(current.RowVersion + 1);

        // Re-GET sees the same content.
        var refetched = await client.GetFromJsonAsync<DashboardDto>(
            new Uri("/api/v1/dashboards/default", UriKind.Relative),
            JsonOptions);
        refetched.ShouldNotBeNull();
        refetched.LayoutJson.ShouldBe(save.LayoutJson);
        refetched.RowVersion.ShouldBe(saved.RowVersion);
    }

    [Fact]
    public async Task Put_Default_With_Stale_RowVersion_Returns_409()
    {
        using var client = _fixture.CreateClient();

        var current = await client.GetFromJsonAsync<DashboardDto>(
            new Uri("/api/v1/dashboards/default", UriKind.Relative),
            JsonOptions);
        current.ShouldNotBeNull();

        var stale = new SaveDashboardRequest(
            Name: "Whatever",
            LayoutJson: """{"widgets":[]}""",
            RowVersion: current.RowVersion + 999);

        using var response = await client.PutAsJsonAsync(
            new Uri("/api/v1/dashboards/default", UriKind.Relative),
            stale,
            JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Put_Default_With_Invalid_Layout_Returns_400()
    {
        using var client = _fixture.CreateClient();

        var current = await client.GetFromJsonAsync<DashboardDto>(
            new Uri("/api/v1/dashboards/default", UriKind.Relative),
            JsonOptions);
        current.ShouldNotBeNull();

        var invalid = new SaveDashboardRequest(
            Name: "Bad",
            LayoutJson: "not json at all",
            RowVersion: current.RowVersion);

        using var response = await client.PutAsJsonAsync(
            new Uri("/api/v1/dashboards/default", UriKind.Relative),
            invalid,
            JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_Default_With_Empty_Name_Returns_400()
    {
        using var client = _fixture.CreateClient();

        var current = await client.GetFromJsonAsync<DashboardDto>(
            new Uri("/api/v1/dashboards/default", UriKind.Relative),
            JsonOptions);
        current.ShouldNotBeNull();

        var invalid = new SaveDashboardRequest(
            Name: "   ",
            LayoutJson: """{"widgets":[]}""",
            RowVersion: current.RowVersion);

        using var response = await client.PutAsJsonAsync(
            new Uri("/api/v1/dashboards/default", UriKind.Relative),
            invalid,
            JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_Default_With_Oversized_Layout_Returns_400()
    {
        using var client = _fixture.CreateClient();

        var current = await client.GetFromJsonAsync<DashboardDto>(
            new Uri("/api/v1/dashboards/default", UriKind.Relative),
            JsonOptions);
        current.ShouldNotBeNull();

        // Pad the widgets array with markdown content until we cross the byte cap.
        var bigContent = new StringBuilder("""{"widgets":[{"id":"x","kind":"text","x":0,"y":0,"w":1,"h":1,"config":{"markdown":""");
        bigContent.Append('a', DashboardValidation.MaxLayoutJsonBytes + 1);
        bigContent.Append("\"}}]}");

        var oversized = new SaveDashboardRequest(
            Name: "Big",
            LayoutJson: bigContent.ToString(),
            RowVersion: current.RowVersion);

        using var response = await client.PutAsJsonAsync(
            new Uri("/api/v1/dashboards/default", UriKind.Relative),
            oversized,
            JsonOptions);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
