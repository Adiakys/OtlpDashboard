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
    string ScopeName,
    string Name,
    string Kind,
    string? Description,
    string? Unit,
    bool IsMonotonic,
    string Temporality,
    int PointCount);
