namespace OpenTelemetryDashboard.Api.Contracts;

/// <summary>
/// Metadata for a single metric instrument (time-series identity). The
/// four fields <see cref="ResourceHash"/>, <see cref="ScopeName"/>,
/// <see cref="Name"/>, and <see cref="Kind"/> together form the stable
/// lookup key used by <c>GET /api/v1/metrics/points</c>.
/// </summary>
public sealed record InstrumentDto(
    string ResourceHash,
    /// <summary>`service.name` OTel attribute of the resource that pushed this instrument.</summary>
    string? ServiceName,
    /// <summary>`service.instance.id` OTel attribute. Discriminates two instruments with
    /// the same name+scope+service.name that come from different resources — typical for
    /// collector receivers that scrape multiple databases / hosts under the same logical
    /// service name.</summary>
    string? ServiceInstanceId,
    string ScopeName,
    string Name,
    string Kind,
    string? Description,
    string? Unit,
    bool IsMonotonic,
    string Temporality,
    int PointCount);
