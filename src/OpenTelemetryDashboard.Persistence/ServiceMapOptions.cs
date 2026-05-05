namespace OpenTelemetryDashboard.Persistence;

/// <summary>
/// Tunes how the service-map reader synthesises external-dependency
/// nodes from <c>kind=Client</c> spans. Bound at startup from
/// <see cref="SectionName"/>; consumers read it via
/// <c>IOptionsMonitor&lt;ServiceMapOptions&gt;</c> so config reloads
/// take effect without a restart.
///
/// <para>
/// Each key in <see cref="DependencyAttributes"/> is one detection
/// channel. A span passes through the synthesis when:
/// <list type="bullet">
///   <item>its <c>SpanKind</c> is <c>Client</c>, and</item>
///   <item>at least one of the configured attribute keys is present
///   on its attribute map.</item>
/// </list>
/// The dependency node's display name is the *value* of the matched
/// attribute. With the defaults below, an Npgsql client span with
/// <c>db.system=postgresql</c> produces a <c>postgresql</c> dependency;
/// a Kafka producer span with <c>messaging.system=kafka</c> produces
/// a <c>kafka</c> dependency. No service-name whitelist anywhere —
/// the topology is emergent from the data.
/// </para>
///
/// <para>
/// Operators can extend this list to surface custom downstream tiers
/// (e.g. <c>company.gateway</c>, <c>rpc.system</c>) without touching
/// code; setting it to an empty array disables synthesis entirely.
/// </para>
/// </summary>
public sealed class ServiceMapOptions
{
    public const string SectionName = "Dashboard:ServiceMap";

    /// <summary>
    /// OTel attribute keys whose presence on a <c>kind=Client</c> span
    /// marks it as a call to an external dependency. The value of the
    /// attribute names the dependency node. Defaults cover the most
    /// common OTel semantic-convention slots:
    /// <list type="bullet">
    ///   <item><c>db.system</c> — databases (postgresql, redis, mongodb, …)</item>
    ///   <item><c>messaging.system</c> — queues / brokers (kafka, rabbitmq, …)</item>
    /// </list>
    /// Legacy <c>http.host</c> is a candidate to add manually for ingestion
    /// from pre-OTel-1.21 SDKs, but it shouldn't appear alongside
    /// <c>server.address</c> in modern data.
    /// </summary>
    public string[] DependencyAttributes { get; set; } =
    [
        "db.system",
        "messaging.system"
    ];
}
