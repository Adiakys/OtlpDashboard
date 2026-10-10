using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Api.Contracts;
using OpenTelemetryDashboard.Core.Abstractions;

namespace OpenTelemetryDashboard.Api.Endpoints;

/// <summary>
/// Query-string binding for the windowed /services endpoints shared by logs
/// and traces. Metrics use a parameterless handler — the set of recorded
/// instruments is the current truth, scoped by retention.
/// </summary>
internal sealed record ServicesQueryParameters(
    [FromQuery(Name = "from")] DateTimeOffset? From,
    [FromQuery(Name = "to")] DateTimeOffset? To);

/// <summary>
/// HTTP handlers that drive the "Application" filter in the UI. Logs and
/// traces return each <c>service.name</c> in the window with its
/// <c>service.instance.id</c> values; metrics return plain names.
/// </summary>
internal static class ServicesEndpoints
{
    public static async Task<Results<Ok<IReadOnlyList<ServiceInstancesDto>>, ValidationProblem>> GetLogServicesAsync(
        [AsParameters] ServicesQueryParameters parameters,
        ILogReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!QueryValidation.TryValidateServicesWindow(parameters, options.Value, out var from, out var to, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await GroupInstancesAsync(
            reader.GetDistinctServicesAsync(from, to, cancellationToken)).ConfigureAwait(false));
    }

    public static async Task<Results<Ok<IReadOnlyList<ServiceInstancesDto>>, ValidationProblem>> GetTraceServicesAsync(
        [AsParameters] ServicesQueryParameters parameters,
        ITraceReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!QueryValidation.TryValidateServicesWindow(parameters, options.Value, out var from, out var to, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        return TypedResults.Ok(await GroupInstancesAsync(
            reader.GetDistinctServicesAsync(from, to, cancellationToken)).ConfigureAwait(false));
    }

    private static async Task<IReadOnlyList<ServiceInstancesDto>> GroupInstancesAsync(
        IAsyncEnumerable<(string ServiceName, string? InstanceId)> rows)
    {
        var byService = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        await foreach (var (serviceName, instanceId) in rows.ConfigureAwait(false))
        {
            if (!byService.TryGetValue(serviceName, out var instances))
            {
                instances = new SortedSet<string>(StringComparer.Ordinal);
                byService[serviceName] = instances;
            }
            if (!string.IsNullOrEmpty(instanceId)) instances.Add(instanceId);
        }

        return [.. byService.Select(kv => new ServiceInstancesDto(kv.Key, [.. kv.Value]))];
    }

    public static async Task<Ok<IReadOnlyList<string>>> GetMetricServicesAsync(
        IMetricReader reader,
        CancellationToken cancellationToken)
    {
        var raw = await reader.GetDistinctServiceNamesAsync(cancellationToken).ConfigureAwait(false);
        var names = new SortedSet<string>(raw, StringComparer.Ordinal);
        return TypedResults.Ok<IReadOnlyList<string>>([.. names]);
    }
}
