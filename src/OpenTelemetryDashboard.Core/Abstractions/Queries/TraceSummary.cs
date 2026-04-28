using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Core.Abstractions.Queries;

/// <summary>
/// Read-side projection used by trace listings: one row per trace, aggregated
/// from its spans. The root span contributes <see cref="RootSpanName"/> and
/// <see cref="RootStatusCode"/>; if no explicit root is present, the earliest
/// span is used.
/// </summary>
public sealed class TraceSummary
{
    public required TraceId TraceId { get; init; }
    public required byte[] ResourceHash { get; init; }
    public required string RootSpanName { get; init; }
    public required long StartUnixNano { get; init; }
    public required long EndUnixNano { get; init; }
    public int SpanCount { get; init; }
    public SpanStatusCode RootStatusCode { get; init; }
}
