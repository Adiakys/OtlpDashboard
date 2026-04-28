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
public sealed class ResourceFkIntegrityOnPostgreSqlTests : MultiProviderTestBase<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public Task Cannot_delete_resource_with_dependent_spans() =>
        ResourceFkIntegrityAssertions.AssertRestrict(Host!);
}

[Collection("MultiProvider")]
public sealed class ResourceFkIntegrityOnSqlServerTests : MultiProviderTestBase<SqlServerDatabaseFixture>
{
    [SkippableFact]
    public Task Cannot_delete_resource_with_dependent_spans() =>
        ResourceFkIntegrityAssertions.AssertRestrict(Host!);
}

internal static class ResourceFkIntegrityAssertions
{
    public static async Task AssertRestrict(ProviderTestHostFixture host)
    {
        // Arrange — write a Resource + Span via the sink (so the FK is in place)
        var resourceHash = ResourceHasher.Compute(
            serviceName: "fk-svc",
            serviceInstanceId: null,
            schemaUrl: null,
            droppedAttributesCount: 0,
            attributes: AttributeMap.Empty);

        var resource = new Resource
        {
            Hash = resourceHash,
            ServiceName = "fk-svc",
        };

        var span = new Span
        {
            TraceId = TraceId.FromBytes(new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 }),
            SpanId = SpanId.FromBytes(new byte[8] { 1, 2, 3, 4, 5, 6, 7, 8 }),
            ResourceHash = resourceHash,
            Name = "fk-span",
            StartUnixNano = 1,
            EndUnixNano = 2,
        };

        var sink = host.Services.GetRequiredService<ITraceSink>();
        await sink.WriteAsync(new[] { new TraceBatch([resource], [span]) }, CancellationToken.None);

        // Act — try to delete the Resource that the Span references
        var factory = host.Services.GetRequiredService<IDbContextFactory<TelemetryDbContext>>();
        await using var ctx = await factory.CreateDbContextAsync();
        var tracked = await ctx.Resources.SingleAsync(r => r.Hash == resourceHash);
        ctx.Resources.Remove(tracked);

        // Assert — DbUpdateException with inner DB-specific exception
        var ex = await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        ex.InnerException.ShouldNotBeNull();
    }
}
