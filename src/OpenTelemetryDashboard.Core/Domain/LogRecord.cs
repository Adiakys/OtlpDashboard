namespace OpenTelemetryDashboard.Core.Domain;

public sealed class LogRecord
{
    public required byte[] ResourceHash { get; init; }

    public long TimeUnixNano { get; init; }
    public long ObservedTimeUnixNano { get; init; }
    public SeverityNumber SeverityNumber { get; init; }
    public string? SeverityText { get; init; }
    public string? Body { get; init; }

    public TraceId TraceId { get; init; }
    public SpanId SpanId { get; init; }
    public uint Flags { get; init; }

    public string? ScopeName { get; init; }
    public string? ScopeVersion { get; init; }

    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = AttributeMap.Empty;
    public uint DroppedAttributesCount { get; init; }
}
