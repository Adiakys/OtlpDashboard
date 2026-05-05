namespace OpenTelemetryDashboard.Api.Contracts;

/// <summary>
/// Wire shape for <c>GET /api/v1/traces/service-map</c>. Each node is a
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
    /// synthesised external entities (databases, caches) inferred from
    /// kind=Client + db.system spans.</summary>
    string Kind,
    long RequestCount,
    long ErrorCount);

public sealed record ServiceMapEdgeDto(
    string FromService,
    string ToService,
    long CallCount,
    long ErrorCount);
