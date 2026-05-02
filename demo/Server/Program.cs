using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SampleServer;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration ---------------------------------------------------------

const string serviceName = "sample-server";
const string serviceNamespace = "oteldemo";
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? "Host=localhost;Database=telemetry;Username=otel;Password=otel";
var redisConnection = builder.Configuration.GetConnectionString("Redis")
    ?? "localhost:6379";
var otlpEndpoint = builder.Configuration["Otel:Endpoint"]
    ?? "http://localhost:4317";
var otlpHeaders = builder.Configuration["Otel:Headers"]
    ?? string.Empty;
// Stable `service.instance.id` so widgets pinned to this instance survive
// container restarts. The OTel SDK otherwise generates a fresh GUID on
// every boot, which would break instance-specific widget bindings.
var serviceInstanceId = builder.Configuration["Otel:ServiceInstanceId"]
    ?? "server-1";

// ---- DbContext + HybridCache ----------------------------------------------

builder.Services.AddDbContextPool<CounterDbContext>(opt =>
    opt.UseNpgsql(connectionString));
builder.Services.AddDbContextFactory<CounterDbContext>(opt =>
    opt.UseNpgsql(connectionString));

builder.Services.AddStackExchangeRedisCache(opt =>
{
    opt.Configuration = redisConnection;
    opt.InstanceName = $"{serviceName}:";
});
builder.Services.AddHybridCache(opt =>
{
    opt.DefaultEntryOptions = new HybridCacheEntryOptions
    {
        Expiration = TimeSpan.FromSeconds(30),
        LocalCacheExpiration = TimeSpan.FromSeconds(10)
    };
});

// Trace instrumentation for the Redis client uses the same multiplexer
// the cache talks to — so trace spans show the GET/SET roundtrips.
var redisMultiplexer = await ConnectionMultiplexer.ConnectAsync(redisConnection);
builder.Services.AddSingleton<IConnectionMultiplexer>(redisMultiplexer);

// ---- OpenTelemetry ---------------------------------------------------------

void ConfigureOtlp(OpenTelemetry.Exporter.OtlpExporterOptions opt)
{
    opt.Endpoint = new Uri(otlpEndpoint);
    if (!string.IsNullOrWhiteSpace(otlpHeaders))
    {
        opt.Headers = otlpHeaders;
    }
}

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r
        .AddService(serviceName, serviceNamespace, "1.0.0",
            autoGenerateServiceInstanceId: false,
            serviceInstanceId: serviceInstanceId)
        .AddAttributes([new KeyValuePair<string, object>("deployment.environment", "demo")]))
    .WithTracing(t => t
        .AddSource(CounterService.ActivitySourceName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()
        .AddRedisInstrumentation(redisMultiplexer)
        .AddOtlpExporter(ConfigureOtlp))
    .WithMetrics(m => m
        .AddMeter(CounterService.MeterName)
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(ConfigureOtlp));

builder.Logging.AddOpenTelemetry(o =>
{
    o.IncludeFormattedMessage = true;
    o.IncludeScopes = true;
    o.AddOtlpExporter(ConfigureOtlp);
});

// ---- Domain services -------------------------------------------------------

builder.Services.AddSingleton<CounterService>();

var app = builder.Build();

// ---- Schema bootstrap ------------------------------------------------------

// The compose `postgres-init` step creates an empty `sampleapp` DB, so
// EnsureCreatedAsync sees no tables and provisions the context's schema
// from the model. It's a no-op on every subsequent boot.
using (var scope = app.Services.CreateScope())
{
    var ctx = scope.ServiceProvider.GetRequiredService<CounterDbContext>();
    await ctx.Database.EnsureCreatedAsync();
    if (!await ctx.Counters.AnyAsync())
    {
        ctx.Counters.Add(new Counter { Id = 1, Value = 0, UpdatedAt = DateTimeOffset.UtcNow });
        await ctx.SaveChangesAsync();
    }
}

// ---- Endpoints -------------------------------------------------------------

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

app.MapGet("/counter", async (CounterService svc, CancellationToken ct) =>
{
    var value = await svc.GetAsync(ct);
    return Results.Ok(new { value });
});

app.MapPost("/counter/random", async (CounterService svc, CancellationToken ct) =>
{
    var (oldValue, newValue, delta) = await svc.MutateRandomAsync(ct);
    return Results.Ok(new { oldValue, newValue, delta });
});

app.MapPost("/counter/{value:int}", async (int value, CounterService svc, CancellationToken ct) =>
{
    var newValue = await svc.SetAsync(value, ct);
    return Results.Ok(new { value = newValue });
});

app.Logger.LogInformation("sample-server listening — postgres={Postgres}, redis={Redis}, otlp={Otlp}",
    connectionString, redisConnection, otlpEndpoint);

app.Run();
