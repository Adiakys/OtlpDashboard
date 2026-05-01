using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Api;
using OpenTelemetryDashboard.Core;
using OpenTelemetryDashboard.Dashboards;
using OpenTelemetryDashboard.Dashboards.Seeding;
using OpenTelemetryDashboard.Host.Authentication;
using OpenTelemetryDashboard.Host.Configuration;
using OpenTelemetryDashboard.Ingestion;
using OpenTelemetryDashboard.Ingestion.Http;
using OpenTelemetryDashboard.Persistence;
using OpenTelemetryDashboard.Persistence.Sqlite;
using OpenTelemetryDashboard.Persistence.SqlServer;
using OpenTelemetryDashboard.Persistence.PostgreSql;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

builder.Services.AddIngestionServerOptions(builder.Configuration);
builder.Services.AddStorageOptions(builder.Configuration);

// Reads of IngestionServerOptions / StorageOptions below go through closures
// that run at Build time (or later, at DI resolution). By then the final
// configuration — including test overrides added via ConfigureAppConfiguration
// — is in place on builder.Configuration. Avoid inline reads: those see the
// pre-override defaults and silently bypass integration-test isolation.

builder.WebHost.ConfigureKestrel((context, options) =>
{
    var ingestion = context.Configuration
        .GetSection(IngestionServerOptions.SectionName)
        .Get<IngestionServerOptions>() ?? new IngestionServerOptions();

    options.Limits.MaxRequestBodySize = ingestion.Http.MaxRequestBodySize;
    options.ListenAnyIP(ingestion.Grpc.Port, listen => listen.Protocols = HttpProtocols.Http2);
    options.ListenAnyIP(ingestion.Http.Port, listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
});

builder.Services.AddGrpc(grpc =>
{
    var ingestion = builder.Configuration
        .GetSection(IngestionServerOptions.SectionName)
        .Get<IngestionServerOptions>() ?? new IngestionServerOptions();

    grpc.MaxReceiveMessageSize = ingestion.Grpc.MaxReceiveMessageSize;
    grpc.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

builder.Services.AddRoutingCore();
builder.Services.AddRateLimiter(rate =>
{
    var ingestion = builder.Configuration
        .GetSection(IngestionServerOptions.SectionName)
        .Get<IngestionServerOptions>() ?? new IngestionServerOptions();

    rate.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rate.AddFixedWindowLimiter("otlp-http", o =>
    {
        o.PermitLimit = ingestion.Http.RateLimit.PermitsPerSecond;
        o.Window = TimeSpan.FromSeconds(1);
        o.QueueLimit = ingestion.Http.RateLimit.Burst;
        o.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

builder.Services.AddTelemetryCore(builder.Configuration);
builder.Services.AddOtlpIngestion();

// Storage provider is the one value we MUST know at registration time — it
// dictates which DI extensions are wired. Connection string is resolved
// lazily via IConfiguration so test fixtures can override it.
var storageProvider = builder.Configuration
    .GetValue<StorageProvider>($"{StorageOptions.SectionName}:{nameof(StorageOptions.Provider)}");

switch (storageProvider)
{
    case StorageProvider.Sqlite:
        builder.Services.AddSqliteTelemetryStore(ResolveConnectionString("Sqlite"));
        break;
    case StorageProvider.SqlServer:
        builder.Services.AddSqlServerTelemetryStore(ResolveConnectionString("SqlServer"));
        break;
    case StorageProvider.PostgreSql:
        builder.Services.AddPostgreSqlTelemetryStore(ResolveConnectionString("PostgreSql"));
        break;
    default:
        throw new InvalidOperationException(
            $"Storage provider '{storageProvider}' is not supported in this build.");
}

static Func<IServiceProvider, string> ResolveConnectionString(string name) =>
    sp =>
    {
        var cs = sp.GetRequiredService<IConfiguration>().GetConnectionString(name);
        if (string.IsNullOrWhiteSpace(cs))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{name} is missing or empty in configuration.");
        }
        return cs;
    };

builder.Services.AddTelemetryWriter();

builder.Services.AddTelemetryRetention(builder.Configuration);

builder.Services.AddQueryApi(builder.Configuration);

builder.Services.AddDashboards(builder.Configuration);

builder.Services.AddDashboardAuth(builder.Configuration);

builder.Services.AddHealthChecks();

builder.Host.ConfigureHostOptions(o =>
{
    var ingestion = builder.Configuration
        .GetSection(IngestionServerOptions.SectionName)
        .Get<IngestionServerOptions>() ?? new IngestionServerOptions();

    // Give the background writer enough time to drain the telemetry channel.
    o.ShutdownTimeout = TimeSpan.FromSeconds(ingestion.Shutdown.DrainTimeoutSeconds + 5);
});

var app = builder.Build();

// Apply EF Core migrations on every boot. Safe: SQLite provider's migrator is
// idempotent, we run a single writer process, and the cost on an up-to-date
// schema is a single metadata query. Production containers rely on this to
// create the schema on first run.
await using (var scope = app.Services.CreateAsyncScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
    await using var context = await factory.CreateDbContextAsync();
    await context.Database.MigrateAsync();

    // Seed built-in dashboards from filesystem after the schema is ready.
    // Idempotent: an id already in the store is skipped silently, so this
    // safe to run on every boot.
    var seeder = scope.ServiceProvider.GetRequiredService<IBuiltinDashboardSeeder>();
    await seeder.SeedAsync(CancellationToken.None);
}

app.UseRateLimiter();

// Serve the Nuxt SPA (built with `nuxi generate`) from wwwroot/. In dev the
// folder may be empty — Nuxt is served on its own port via dev proxy; the
// static middleware just no-ops and requests fall through to the endpoints
// below. In prod the Dockerfile copies the SPA output here.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Surface the opt-in auth posture at startup so operators aren't surprised
// that unset env vars leave endpoints public.
var auth = app.Services.GetRequiredService<IOptions<DashboardAuthOptions>>().Value;
var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Dashboard.Auth");
if (string.IsNullOrEmpty(auth.BrowserToken))
{
    startupLogger.BrowserTokenNotSet();
}
if (string.IsNullOrEmpty(auth.Otlp.ApiKey))
{
    startupLogger.OtlpApiKeyNotSet();
}

app.MapOtlpGrpcServices(
    conv => conv.RequireAuthorization(AuthServiceCollectionExtensions.OtlpIngestPolicy));
app.MapOtlpHttpEndpoints()
    .RequireAuthorization(AuthServiceCollectionExtensions.OtlpIngestPolicy)
    .RequireRateLimiting("otlp-http");

app.MapQueryApi().RequireAuthorization(AuthServiceCollectionExtensions.ReadApiPolicy);
app.MapDashboards().RequireAuthorization(AuthServiceCollectionExtensions.ReadApiPolicy);
app.MapWidgets().RequireAuthorization(AuthServiceCollectionExtensions.ReadApiPolicy);
app.MapDashboardInfo();

app.MapHealthChecks("/healthz").AllowAnonymous();

// SPA client-side routing: any non-API request that didn't match an endpoint
// or a static file falls back to index.html, which then hydrates Vue Router.
// The SPA shell itself is public so the eventual login form can render.
app.MapFallbackToFile("index.html");

await app.RunAsync();

/// <summary>
/// Marker type so integration tests can target this assembly via <c>WebApplicationFactory&lt;Program&gt;</c>.
/// </summary>
public partial class Program;
