using OpenTelemetryDashboard.Api.Contracts;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Api.Mappings;

/// <summary>
/// Domain <see cref="LogRecord"/> → <see cref="LogRecordDto"/> projection.
/// Pure mapping — no I/O, no validation.
/// </summary>
internal static class LogMappings
{
    public static LogRecordDto ToDto(this LogRecord record, string? serviceName)
    {
        ArgumentNullException.ThrowIfNull(record);

        return new LogRecordDto(
            Time: UnixNanoTime.FromUnixNanoseconds(record.TimeUnixNano),
            ObservedTime: record.ObservedTimeUnixNano == 0
                ? null
                : UnixNanoTime.FromUnixNanoseconds(record.ObservedTimeUnixNano),
            SeverityNumber: (int)record.SeverityNumber,
            SeverityText: record.SeverityText,
            Body: record.Body,
            TraceId: record.TraceId.IsEmpty ? null : record.TraceId.ToString(),
            SpanId: record.SpanId.IsEmpty ? null : record.SpanId.ToString(),
            ScopeName: record.ScopeName,
            ScopeVersion: record.ScopeVersion,
            ResourceHash: Convert.ToHexStringLower(record.ResourceHash),
            ServiceName: serviceName,
            Attributes: record.Attributes);
    }
}
