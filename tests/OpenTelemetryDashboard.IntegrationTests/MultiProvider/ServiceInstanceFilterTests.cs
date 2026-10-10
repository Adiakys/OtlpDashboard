using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Core.Abstractions;
using OpenTelemetryDashboard.Core.Abstractions.Queries;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Hashing;
using OpenTelemetryDashboard.Core.Ingestion;
using OpenTelemetryDashboard.IntegrationTests.Fixtures;
using Xunit;

namespace OpenTelemetryDashboard.IntegrationTests.MultiProvider;

[Collection("MultiProvider")]
public sealed class ServiceInstanceFilterOnPostgreSqlTests : MultiProviderTestBase<PostgreSqlDatabaseFixture>
{
    [SkippableFact]
    public Task Instance_filter_and_listing_translate() =>
        ServiceInstanceFilterAssertions.AssertAsync(Host!);
}

[Collection("MultiProvider")]
public sealed class ServiceInstanceFilterOnSqlServerTests : MultiProviderTestBase<SqlServerDatabaseFixture>
{
    [SkippableFact]
    public Task Instance_filter_and_listing_translate() =>
        ServiceInstanceFilterAssertions.AssertAsync(Host!);
}

internal static class ServiceInstanceFilterAssertions
{
    public static async Task AssertAsync(ProviderTestHostFixture host)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var api = $"api-{suffix}";
        var workerName = $"worker-{suffix}";
        var api1 = NewResource(api, "host:8080");
        var api2 = NewResource(api, "host:8081");
        var worker = NewResource(workerName, "host:8080");
        var time = new DateTimeOffset(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        var records = new[] { api1, api2, worker }.Select((r, i) => new LogRecord
        {
            ResourceHash = r.Hash,
            TimeUnixNano = UnixNanoTime.ToUnixNanoseconds(time.AddSeconds(i)),
            SeverityNumber = SeverityNumber.Info,
            Body = $"{r.ServiceName}|{r.ServiceInstanceId}",
        }).ToList();

        await host.Services.GetRequiredService<ILogSink>().WriteAsync(
            [new LogBatch([api1, api2, worker], records)],
            CancellationToken.None);

        var reader = host.Services.GetRequiredService<ILogReader>();
        var query = new LogQuery(
            time.AddMinutes(-1), time.AddMinutes(1), 100, null,
            ServiceNames: [workerName, $"{api}:host:8081"]);

        var bodies = new List<string?>();
        await foreach (var row in reader.QueryAsync(query, CancellationToken.None))
        {
            bodies.Add(row.Record.Body);
        }
        bodies.ShouldBe([$"{workerName}|host:8080", $"{api}|host:8081"], ignoreOrder: true);

        var pairs = new List<(string, string?)>();
        await foreach (var pair in reader.GetDistinctServicesAsync(time.AddMinutes(-1), time.AddMinutes(1), CancellationToken.None))
        {
            if (pair.ServiceName.EndsWith(suffix, StringComparison.Ordinal)) pairs.Add(pair);
        }
        pairs.ShouldBe([(api, "host:8080"), (api, "host:8081"), (workerName, "host:8080")], ignoreOrder: true);
    }

    private static Resource NewResource(string service, string instanceId) => new()
    {
        Hash = ResourceHasher.Compute(service, instanceId, schemaUrl: null, droppedAttributesCount: 0, AttributeMap.Empty),
        ServiceName = service,
        ServiceInstanceId = instanceId,
    };
}
