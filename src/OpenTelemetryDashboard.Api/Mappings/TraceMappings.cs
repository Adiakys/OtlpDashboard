using OpenTelemetryDashboard.Api.Contracts;
using OpenTelemetryDashboard.Core.Abstractions.Queries;
using OpenTelemetryDashboard.Core.Common;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Api.Mappings;

/// <summary>
/// Domain → DTO projections for the trace-related endpoints:
/// <see cref="TraceSummary"/> → <see cref="TraceSummaryDto"/> (listings) and
/// <see cref="Span"/> → <see cref="SpanDto"/> (details), plus the owned
/// <c>Event</c>/<c>Link</c> collections. Pure mapping.
/// </summary>
internal static class TraceMappings
{
    public static TraceSummaryDto ToDto(this TraceSummary summary, string? serviceName)
    {
        ArgumentNullException.ThrowIfNull(summary);

        var start = UnixNanoTime.FromUnixNanoseconds(summary.StartUnixNano);
        var end = UnixNanoTime.FromUnixNanoseconds(summary.EndUnixNano);

        return new TraceSummaryDto(
            TraceId: summary.TraceId.ToString(),
            RootSpanName: summary.RootSpanName,
            Start: start,
            End: end,
            DurationMs: (end - start).TotalMilliseconds,
            SpanCount: summary.SpanCount,
            RootStatusCode: summary.RootStatusCode.ToString(),
            ResourceHash: Convert.ToHexStringLower(summary.ResourceHash),
            ServiceName: serviceName);
    }

    public static SpanDto ToDto(this Span span, string? serviceName)
    {
        ArgumentNullException.ThrowIfNull(span);

        var start = UnixNanoTime.FromUnixNanoseconds(span.StartUnixNano);
        var end = UnixNanoTime.FromUnixNanoseconds(span.EndUnixNano);

        var events = new List<SpanEventDto>(span.Events.Count);
        foreach (var e in span.Events)
        {
            events.Add(new SpanEventDto(
                Name: e.Name,
                Time: UnixNanoTime.FromUnixNanoseconds(e.TimeUnixNano),
                Attributes: e.Attributes));
        }

        var links = new List<SpanLinkDto>(span.Links.Count);
        foreach (var l in span.Links)
        {
            links.Add(new SpanLinkDto(
                TraceId: l.TraceId.ToString(),
                SpanId: l.SpanId.ToString(),
                Attributes: l.Attributes));
        }

        return new SpanDto(
            SpanId: span.SpanId.ToString(),
            ParentSpanId: span.ParentSpanId is { } p && !p.IsEmpty ? p.ToString() : null,
            Name: span.Name,
            Kind: span.Kind.ToString(),
            Start: start,
            End: end,
            DurationMs: (end - start).TotalMilliseconds,
            StatusCode: span.StatusCode.ToString(),
            StatusMessage: span.StatusMessage,
            ScopeName: span.ScopeName,
            ScopeVersion: span.ScopeVersion,
            ServiceName: serviceName,
            Attributes: span.Attributes,
            Events: events,
            Links: links);
    }
}
