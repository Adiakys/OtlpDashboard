using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.IntegrationTests;

/// <summary>
/// Behaviour of the opt-in MCP server: gated by Dashboard:Mcp:Enabled, mounted
/// at /mcp, and protected by the read-API authorization policy. The shared
/// <see cref="TestHostFixture"/> intentionally keeps MCP disabled, so the
/// "MCP off" assertion uses it directly while the "MCP on" assertions spin
/// up dedicated WebApplicationFactory instances.
/// </summary>
public sealed class McpServerTests : IAsyncLifetime
{
    private const string BrowserToken = "browser-mcp-vw82";

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(),
        $"oteldash-mcp-{Guid.NewGuid():N}.db");

    private WebApplicationFactory<Program>? _factory;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Dashboard:Storage:Provider"] = "Sqlite",
                        ["ConnectionStrings:Sqlite"] = $"Data Source={_dbPath}",
                        ["Dashboard:BrowserToken"] = BrowserToken,
                        ["Dashboard:Mcp:Enabled"] = "true",
                    });
                });
            });

        _ = _factory.Services;
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var ctx = await dbFactory.CreateDbContextAsync();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
        TempSqliteFiles.TryDelete(_dbPath);
    }

    [Fact]
    public async Task Mcp_Disabled_By_Default_Does_Not_Expose_Mcp()
    {
        // The shared fixture leaves Dashboard:Mcp:Enabled unset → MapDashboardMcp
        // is never called. POST /mcp falls through to the SPA fallback, which is
        // GET-only, so the routing layer responds 405. The point: it's *not*
        // 401 (auth-protected MCP route) and *not* 200 (MCP would have replied).
        await using var fixture = new TestHostFixture();
        await ((IAsyncLifetime)fixture).InitializeAsync();
        try
        {
            using var client = fixture.CreateClient();
            using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");

            using var response = await client.PostAsync(new Uri("/mcp", UriKind.Relative), content);

            response.StatusCode.ShouldBe(HttpStatusCode.MethodNotAllowed);
        }
        finally
        {
            await ((IAsyncLifetime)fixture).DisposeAsync();
        }
    }

    [Fact]
    public async Task Mcp_Enabled_Without_Token_Returns_401()
    {
        using var client = _factory!.CreateClient();
        using var content = new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"0.0"}}}""",
            System.Text.Encoding.UTF8,
            "application/json");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");

        using var response = await client.PostAsync(new Uri("/mcp", UriKind.Relative), content);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mcp_Enabled_With_Bearer_Lists_Tools()
    {
        using var httpClient = _factory!.CreateClient();
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", BrowserToken);

        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = new Uri(httpClient.BaseAddress!, "/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            Name = "OtelDashboardMcpTest",
        };

        await using var transport = new HttpClientTransport(
            transportOptions,
            httpClient,
            loggerFactory: null,
            ownsHttpClient: false);

        await using var mcpClient = await McpClient.CreateAsync(
            transport,
            clientOptions: null,
            loggerFactory: null,
            cancellationToken: CancellationToken.None);

        var tools = await mcpClient.ListToolsAsync(
            cancellationToken: CancellationToken.None);

        tools.ShouldNotBeEmpty();
        var names = tools.Select(t => t.Name).ToHashSet(StringComparer.Ordinal);
        names.ShouldContain("query_logs");
        names.ShouldContain("query_traces");
        names.ShouldContain("get_trace");
        names.ShouldContain("list_metrics");
        names.ShouldContain("query_metric_points");
    }
}
