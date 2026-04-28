namespace OpenTelemetryDashboard.Api.Contracts;

public sealed record LogRecordDto(
    DateTimeOffset Time,
    DateTimeOffset? ObservedTime,
    int SeverityNumber,
    string? SeverityText,
    string? Body,
    string? TraceId,
    string? SpanId,
    string? ScopeName,
    string? ScopeVersion,
    string ResourceHash,
    /// <summary>`service.name` OTel attribute of the associated resource, or null if unset.</summary>
    string? ServiceName,
    IReadOnlyDictionary<string, object?> Attributes);
