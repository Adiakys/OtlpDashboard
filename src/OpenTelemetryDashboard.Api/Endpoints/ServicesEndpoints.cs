using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Core.Abstractions;

namespace OpenTelemetryDashboard.Api.Endpoints;

/// <summary>
/// Query-string binding for the windowed /services endpoints shared by logs
/// and traces. Metrics don't need a window (the in-memory ring buffer IS the
/// current truth) so they use a parameterless handler.
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

    public static Ok<IReadOnlyList<string>> GetMetricServices(IMetricReader reader)
    {
        var names = new SortedSet<string>(reader.GetDistinctServiceNames(), StringComparer.Ordinal);
        return TypedResults.Ok<IReadOnlyList<string>>([.. names]);
    }
}
