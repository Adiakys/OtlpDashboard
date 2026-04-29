using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
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
/// HTTP handlers that drive the "Application" filter in the UI. Each one
/// returns the distinct, alphabetically-sorted, non-null set of
/// <c>service.name</c> values currently visible to its domain reader.
/// </summary>
internal static class ServicesEndpoints
{
    public static async Task<Results<Ok<IReadOnlyList<string>>, ValidationProblem>> GetLogServicesAsync(
        [AsParameters] ServicesQueryParameters parameters,
        ILogReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!QueryValidation.TryValidateServicesWindow(parameters, options.Value, out var from, out var to, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var names = new SortedSet<string>(StringComparer.Ordinal);
        await foreach (var name in reader.GetDistinctServiceNamesAsync(from, to, cancellationToken).ConfigureAwait(false))
        {
            names.Add(name);
        }

        return TypedResults.Ok<IReadOnlyList<string>>([.. names]);
    }

    public static async Task<Results<Ok<IReadOnlyList<string>>, ValidationProblem>> GetTraceServicesAsync(
        [AsParameters] ServicesQueryParameters parameters,
        ITraceReader reader,
        IOptions<QueryApiOptions> options,
        CancellationToken cancellationToken)
    {
        if (!QueryValidation.TryValidateServicesWindow(parameters, options.Value, out var from, out var to, out var errors))
        {
            return TypedResults.ValidationProblem(errors);
        }

        var names = new SortedSet<string>(StringComparer.Ordinal);
        await foreach (var name in reader.GetDistinctServiceNamesAsync(from, to, cancellationToken).ConfigureAwait(false))
        {
            names.Add(name);
        }

        return TypedResults.Ok<IReadOnlyList<string>>([.. names]);
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
