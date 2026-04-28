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
    string? ServiceName);
