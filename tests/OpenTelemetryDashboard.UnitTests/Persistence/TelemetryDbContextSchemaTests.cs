using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Hashing;
using OpenTelemetryDashboard.Persistence;

namespace OpenTelemetryDashboard.UnitTests.Persistence;

public sealed class TelemetryDbContextSchemaTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public TelemetryDbContextSchemaTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    private TelemetryDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TelemetryDbContext>()
            .UseSqlite(_connection)
            .Options;
        var context = new TelemetryDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task Can_Persist_And_Read_Resource()
    {
        await using var context = CreateContext();
        var hash = ResourceHasher.Compute("svc", "i1", null, 0, AttributeMap.Empty);
        context.Resources.Add(new Resource
        {
            Hash = hash,
            ServiceName = "svc",
            ServiceInstanceId = "i1",
            Attributes = new Dictionary<string, object?> { ["env"] = "test" },
        });
        await context.SaveChangesAsync();

        var loaded = await context.Resources.AsNoTracking().FirstAsync();
        loaded.ServiceName.ShouldBe("svc");
        loaded.Attributes["env"].ShouldBe("test");
        loaded.Hash.Length.ShouldBe(ResourceHasher.HashSizeInBytes);
    }

    [Fact]
    public async Task Can_Persist_Span_With_Events_And_Links()
    {
        await using var context = CreateContext();
        var hash = ResourceHasher.Compute("svc", null, null, 0, AttributeMap.Empty);
        context.Resources.Add(new Resource { Hash = hash, ServiceName = "svc" });

        var trace = TraceId.FromBytes(RandomBytes(16));
        var spanId = SpanId.FromBytes(RandomBytes(8));

        var span = new Span
        {
            TraceId = trace,
            SpanId = spanId,
            ResourceHash = hash,
            Name = "GET /orders",
            Kind = SpanKind.Server,
            StartUnixNano = 1,
            EndUnixNano = 42,
            StatusCode = SpanStatusCode.Ok,
        };
        span.Events.Add(new SpanEvent { Name = "exception", TimeUnixNano = 10 });
        span.Links.Add(new SpanLink
        {
            TraceId = TraceId.FromBytes(RandomBytes(16)),
            SpanId = SpanId.FromBytes(RandomBytes(8)),
        });
        context.Spans.Add(span);
        await context.SaveChangesAsync();

        var loaded = await context.Spans
            .AsNoTracking()
            .Include(s => s.Events)
            .Include(s => s.Links)
            .FirstAsync();
        loaded.Name.ShouldBe("GET /orders");
        loaded.TraceId.ShouldBe(trace);
        loaded.SpanId.ShouldBe(spanId);
        loaded.Events.Count.ShouldBe(1);
        loaded.Events[0].Name.ShouldBe("exception");
        loaded.Links.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Can_Persist_LogRecord()
    {
        await using var context = CreateContext();
        var hash = ResourceHasher.Compute("svc", null, null, 0, AttributeMap.Empty);
        context.Resources.Add(new Resource { Hash = hash, ServiceName = "svc" });

        context.Logs.Add(new LogRecord
        {
            ResourceHash = hash,
            TimeUnixNano = 1,
            SeverityNumber = SeverityNumber.Info,
            SeverityText = "INFO",
            Body = "hello",
        });
        await context.SaveChangesAsync();

        var loaded = await context.Logs.AsNoTracking().FirstAsync();
        loaded.Body.ShouldBe("hello");
        loaded.SeverityNumber.ShouldBe(SeverityNumber.Info);
    }

    private static byte[] RandomBytes(int length)
    {
        var b = new byte[length];
        Random.Shared.NextBytes(b);
        return b;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}
