using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Core.Metrics;

/// <summary>
/// Identifies a metric time-series in the in-memory store. Uses the hex representation
/// of the resource hash so that the key is a pure value type with native
/// <see cref="IEquatable{T}"/> and <see cref="object.GetHashCode"/> semantics.
/// </summary>
public readonly record struct InstrumentKey(
    string ResourceHashHex,
    string ScopeName,
    string InstrumentName,
    InstrumentKind Kind)
{
    public static InstrumentKey Create(
        ReadOnlySpan<byte> resourceHash,
        string? scopeName,
        string instrumentName,
        InstrumentKind kind)
    {
        ArgumentNullException.ThrowIfNull(instrumentName);
        return new InstrumentKey(
            Convert.ToHexString(resourceHash).ToLowerInvariant(),
            scopeName ?? string.Empty,
            instrumentName,
            kind);
    }
}
