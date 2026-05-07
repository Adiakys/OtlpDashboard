namespace OpenTelemetryDashboard.Api.Contracts;

public sealed record TraceSummaryDto(
    string TraceId,
    string RootSpanName,
    DateTimeOffset Start,
    DateTimeOffset End,
    double DurationMs,
    int SpanCount,
    string RootStatusCode,
    string ResourceHash,
    /// <summary>Service name of the root span's resource. Null when the caller hasn't set `service.name`.</summary>
    string? ServiceName,
    /// <summary>Distinct service.name values touched by spans of this
    /// trace OTHER than the root's. Empty when every span shares the
    /// root's service. The SPA renders the column as
    /// <c>{ServiceName} (+N)</c> with this list as a tooltip.</summary>
    IReadOnlyList<string> OtherServiceNames);
