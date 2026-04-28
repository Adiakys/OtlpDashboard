using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Abstractions.Retention;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Hashing;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.IntegrationTests.Fixtures;
using OpenTelemetryDashboard.Persistence;
using Xunit;

namespace OpenTelemetryDashboard.IntegrationTests.MultiProvider;

[Collection("MultiProvider")]
public sealed class HighVolumeRetentionOnPostgreSqlTests : MultiProviderTestBase<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public Task Log_retention_deletes_records_older_than_cutoff() =>
        HighVolumeRetentionAssertions.AssertRetention(Host!);
}

[Collection("MultiProvider")]
public sealed class HighVolumeRetentionOnSqlServerTests : MultiProviderTestBase<SqlServerDatabaseFixture>
{
    [SkippableFact]
    public Task Log_retention_deletes_records_older_than_cutoff() =>
        HighVolumeRetentionAssertions.AssertRetention(Host!);
}

internal static class HighVolumeRetentionAssertions
{
    private const int OldCount = 5_000;
    private const int NewCount = 5_000;

    public static async Task AssertRetention(ProviderTestHostFixture host)
    {
        var resourceHash = ResourceHasher.Compute(
            serviceName: "retention-svc",
            serviceInstanceId: null,
            schemaUrl: null,
            droppedAttributesCount: 0,
            attributes: AttributeMap.Empty);

        var resource = new Resource
        {
            Hash = resourceHash,
            ServiceName = "retention-svc",
        };

        // Now and "1 hour ago" — generate timestamps clustered around them.
        var now = DateTimeOffset.UtcNow;
        var nowNano = UnixNanoTime.ToUnixNanoseconds(now);
        var oneHourAgoNano = UnixNanoTime.ToUnixNanoseconds(now - TimeSpan.FromHours(1));

        var logs = new List<LogRecord>(OldCount + NewCount);
        for (int i = 0; i < OldCount; i++)
        {
            logs.Add(new LogRecord
            {
                ResourceHash = resourceHash,
                TimeUnixNano = oneHourAgoNano + i, // tutti < cutoff
                ObservedTimeUnixNano = oneHourAgoNano + i,
                SeverityNumber = SeverityNumber.Info,
                Body = $"old log {i}",
            });
        }
        for (int i = 0; i < NewCount; i++)
        {
            logs.Add(new LogRecord
            {
                ResourceHash = resourceHash,
                TimeUnixNano = nowNano + i, // tutti > cutoff
                ObservedTimeUnixNano = nowNano + i,
                SeverityNumber = SeverityNumber.Info,
                Body = $"new log {i}",
            });
        }

        var sink = host.Services.GetRequiredService<ILogSink>();
        await sink.WriteAsync(new[] { new LogBatch([resource], logs) }, CancellationToken.None);

        var factory = host.Services.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();
        var totalBefore = await ctx.Logs.CountAsync();
        totalBefore.ShouldBe(OldCount + NewCount);

        var policy = host.Services.GetRequiredService<ILogRetentionPolicy>();
        // maxAge of 30 minuti: tutto piu' vecchio di now - 30min va eliminato.
        // I log "old" hanno timestamp ~1 ora fa => verranno eliminati.
        // I log "new" hanno timestamp ~now => sopravvivono.
        var deleted = await policy.EnforceAsync(TimeSpan.FromMinutes(30), CancellationToken.None);
        deleted.ShouldBe(OldCount);

        var totalAfter = await ctx.Logs.CountAsync();
        totalAfter.ShouldBe(NewCount);
    }
}
