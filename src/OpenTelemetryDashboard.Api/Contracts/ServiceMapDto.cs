namespace OpenTelemetryDashboard.Api.Contracts;

/// <summary>
/// Wire shape for <c>GET /api/v1/service-map</c>. Each node is a
/// service touched in the window, each edge a directed call between
/// two services. Self-loops (a service calling itself) are not
/// surfaced — see the reader's rationale.
/// </summary>
public sealed record ServiceMapDto(
    IReadOnlyList<ServiceMapNodeDto> Nodes,
    IReadOnlyList<ServiceMapEdgeDto> Edges);

public sealed record ServiceMapNodeDto(
    string Service,
    /// <summary>"service" for OTel-emitting services; "dependency" for
    /// synthesised external entities (downstream services that don't
    /// emit telemetry of their own) inferred from
    /// <c>kind=Client</c> spans tagged with a peer-service
    /// attribute (<c>peer.service</c> or <c>service.peer.name</c>).</summary>
    string Kind,
    long RequestCount,
    long ErrorCount,
    /// <summary>Only populated on <c>kind="dependency"</c>: the
    /// attribute key (e.g. <c>peer.service</c>) whose value matches
    /// <c>Service</c>. The UI uses it to drill into /traces with a
    /// precise <c>attr=key:value</c> filter — without it the
    /// dependency node has no useful drill-down.</summary>
    string? AttributeKey = null);

public sealed record ServiceMapEdgeDto(
    string FromService,
    string ToService,
    long CallCount,
    long ErrorCount);
