namespace OpenTelemetryDashboard.Api.Contracts;

/// <summary>
/// Envelope for keyset-paginated responses. <see cref="NextCursor"/> is
/// <c>null</c> when there are no further pages.
/// </summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, string? NextCursor);
