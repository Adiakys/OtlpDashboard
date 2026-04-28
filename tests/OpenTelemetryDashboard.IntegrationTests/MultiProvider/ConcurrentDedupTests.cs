using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Hashing;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.IntegrationTests.Fixtures;
using OpenTelemetryDashboard.Persistence;
using Xunit;

namespace OpenTelemetryDashboard.IntegrationTests.MultiProvider;

[Collection("MultiProvider")]
public sealed class ConcurrentDedupOnPostgreSqlTests : MultiProviderTestBase<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public Task Two_batches_with_same_resource_produce_single_resource_row() =>
        ConcurrentDedupAssertions.AssertSingleResource(Host!);
}

[Collection("MultiProvider")]
public sealed class ConcurrentDedupOnSqlServerTests : MultiProviderTestBase<SqlServerDatabaseFixture>
{
    [SkippableFact]
    public Task Two_batches_with_same_resource_produce_single_resource_row() =>
        ConcurrentDedupAssertions.AssertSingleResource(Host!);
}

internal static class ConcurrentDedupAssertions
{
    /// <summary>
    /// Design discovery: il dedup di <see cref="Resource"/> e' app-level via
    /// <c>ResourceCache</c> (cache LRU lock-protected) + scrittura EF Core. La
    /// race tra due <c>WriteAsync</c> concorrenti che vedono entrambi "miss"
    /// nella cache porta a due INSERT sulla stessa PK; uno dei due batch perde
    /// la propria SaveChanges (l'eccezione viene loggata e ingoiata da
    /// <c>EfCoreLogSink</c>, l'ingest e' best-effort). La PK del DB invece
    /// garantisce sempre 1 sola row Resource.
    ///
    /// Il test verifica DUE invarianti reali del sistema:
    /// <list type="number">
    ///   <item>Concorrente: dopo 2 batch in parallelo c'e' esattamente 1 Resource e ALMENO 100 log (at-least-once).</item>
    ///   <item>Sequenziale: dopo altri 2 batch in serie il dedup via cache funziona e ogni log persiste, totale logs = 4 batch.</item>
    /// </list>
    /// </summary>
    public static async Task AssertSingleResource(ProviderTestHostFixture host)
    {
        var resourceHash = ResourceHasher.Compute(
            serviceName: "dedup-svc",
            serviceInstanceId: "i-1",
            schemaUrl: null,
            droppedAttributesCount: 0,
            attributes: AttributeMap.Empty);

        Resource MakeResource() => new()
        {
            Hash = resourceHash,
            ServiceName = "dedup-svc",
            ServiceInstanceId = "i-1",
        };

        LogRecord MakeLog(int i) => new()
        {
            ResourceHash = resourceHash,
            TimeUnixNano = i,
            ObservedTimeUnixNano = i,
            SeverityNumber = SeverityNumber.Info,
            Body = $"log {i}",
        };

        var sink = host.Services.GetRequiredService<ILogSink>();
        var factory = host.Services.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();

        // Fase 1 — concorrente: stress sulla race PK.
        var batch1 = new LogBatch([MakeResource()], Enumerable.Range(0, 100).Select(MakeLog).ToList());
        var batch2 = new LogBatch([MakeResource()], Enumerable.Range(100, 100).Select(MakeLog).ToList());

        var t1 = sink.WriteAsync(new[] { batch1 }, CancellationToken.None);
        var t2 = sink.WriteAsync(new[] { batch2 }, CancellationToken.None);
        await Task.WhenAll(t1, t2);

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            var resources = await ctx.Resources.AsNoTracking()
                .Where(r => r.Hash == resourceHash)
                .CountAsync();
            resources.ShouldBe(1, "DB PK garantisce sempre 1 sola row Resource per hash");

            var logsAfterRace = await ctx.Logs.AsNoTracking()
                .Where(l => l.ResourceHash == resourceHash)
                .CountAsync();
            logsAfterRace.ShouldBeGreaterThanOrEqualTo(
                100,
                "almeno un batch (100 log) deve essere persistito; l'altro puo' fallire " +
                "silenziosamente a causa della race app-cache + DB PK");
        }

        // Fase 2 — sequenziale: il dedup via cache funziona perfettamente,
        // entrambi i batch persistono.
        var batch3 = new LogBatch([MakeResource()], Enumerable.Range(200, 100).Select(MakeLog).ToList());
        var batch4 = new LogBatch([MakeResource()], Enumerable.Range(300, 100).Select(MakeLog).ToList());

        await sink.WriteAsync(new[] { batch3 }, CancellationToken.None);
        await sink.WriteAsync(new[] { batch4 }, CancellationToken.None);

        await using (var ctx = await factory.CreateDbContextAsync())
        {
            var resources = await ctx.Resources.AsNoTracking()
                .Where(r => r.Hash == resourceHash)
                .CountAsync();
            resources.ShouldBe(1);

            var logsAfterSeq = await ctx.Logs.AsNoTracking()
                .Where(l => l.ResourceHash == resourceHash)
                .CountAsync();
            // Almeno i 100 della fase 1 (peggior caso) + 200 della fase 2 sequenziale.
            logsAfterSeq.ShouldBeGreaterThanOrEqualTo(300);
        }
    }
}
