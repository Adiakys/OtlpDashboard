using OpenTelemetryDashboard.Host.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

builder
    .AddOtlpServer()
    .AddDashboardRateLimiting()
    .AddTelemetryStorage()
    .AddDashboardServices();

var app = builder.Build();

await app.RunStartupAsync();
app.UseDashboardPipeline()
   .MapDashboardEndpoints();

await app.RunAsync();

/// <summary>
/// Marker type so integration tests can target this assembly via <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
