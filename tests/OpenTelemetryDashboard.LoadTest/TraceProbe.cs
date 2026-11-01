using System.Diagnostics;
using System.Net;
using OpenTelemetryDashboard.Benchmarks;

namespace OpenTelemetryDashboard.LoadTest;

/// <summary>
/// Sends a single marker span and polls the query API until it shows up:
/// the ingest-to-query delay a user sees on the dashboard. The writer is a
/// single FIFO consumer, so once the marker is visible everything sent
/// before it has been persisted too.
/// </summary>
internal sealed class TraceProbe(OtlpGrpcClient client, HttpClient http, int services)
{
    private readonly OtlpPayloadFactory _factory = new(seed: 99, services);
    private readonly Lock _lock = new();

    public async Task<double?> MeasureAsync(TimeSpan timeout)
    {
        var traceId = new byte[16];
        Random.Shared.NextBytes(traceId);
        traceId[0] |= 1;

        OpenTelemetry.Proto.Collector.Trace.V1.ExportTraceServiceRequest request;
        lock (_lock)
        {
            request = _factory.CreateMarkerTrace(traceId, DateTimeOffset.UtcNow);
        }

        var started = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                await client.ExportAsync(request, CancellationToken.None);
                break;
            }
            catch (Grpc.Core.RpcException) when (started.Elapsed < timeout)
            {
                await Task.Delay(50);
            }
            catch (Grpc.Core.RpcException)
            {
                return null;
            }
        }

        var path = $"/api/v1/traces/{Convert.ToHexStringLower(traceId)}";
        while (started.Elapsed < timeout)
        {
            using var response = await http.GetAsync(path);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return Math.Round(started.Elapsed.TotalMilliseconds, 1);
            }
            await Task.Delay(20);
        }
        return null;
    }
}
