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
public sealed class BinaryRoundtripOnPostgreSqlTests : MultiProviderTestBase<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public Task TraceId_with_NUL_bytes_roundtrips_correctly() =>
        BinaryRoundtripAssertions.RoundtripAssert(Host!);
}

[Collection("MultiProvider")]
public sealed class BinaryRoundtripOnSqlServerTests : MultiProviderTestBase<SqlServerDatabaseFixture>
{
    [SkippableFact]
    public Task TraceId_with_NUL_bytes_roundtrips_correctly() =>
        BinaryRoundtripAssertions.RoundtripAssert(Host!);
}

internal static class BinaryRoundtripAssertions
{
    public static async Task RoundtripAssert(ProviderTestHostFixture host)
    {
        // Arrange — TraceId con NUL bytes in posizioni significative
        var traceIdBytes = new byte[16];
        traceIdBytes[0] = 0x00; traceIdBytes[1] = 0xAB;
        traceIdBytes[7] = 0x00; traceIdBytes[8] = 0xCD;
        traceIdBytes[15] = 0x00;

        var spanIdBytes = new byte[8];
        spanIdBytes[0] = 0x00; spanIdBytes[3] = 0xEF; spanIdBytes[7] = 0x00;

        var resourceHash = ResourceHasher.Compute(
            serviceName: "test-svc-roundtrip",
            serviceInstanceId: null,
            schemaUrl: null,
            droppedAttributesCount: 0,
            attributes: AttributeMap.Empty);

        var resource = new Resource
        {
            Hash = resourceHash,
            ServiceName = "test-svc-roundtrip",
        };

        var span = new Span
        {
            TraceId = TraceId.FromBytes(traceIdBytes),
            SpanId = SpanId.FromBytes(spanIdBytes),
            ResourceHash = resourceHash,
            Name = "roundtrip-span",
            StartUnixNano = 1_000_000_000,
            EndUnixNano = 2_000_000_000,
        };

        var sink = host.Services.GetRequiredService<ITraceSink>();
        await sink.WriteAsync(
            new[] { new TraceBatch([resource], [span]) },
            CancellationToken.None);

        // Assert — riapre il DB e verifica i bytes esatti
        var factory = host.Services.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();

        var stored = await ctx.Spans.AsNoTracking().FirstAsync();
        Convert.ToHexString(stored.TraceId.ToByteArray()).ShouldBe(Convert.ToHexString(traceIdBytes));
        Convert.ToHexString(stored.SpanId.ToByteArray()).ShouldBe(Convert.ToHexString(spanIdBytes));
        stored.Name.ShouldBe("roundtrip-span");
    }
}
