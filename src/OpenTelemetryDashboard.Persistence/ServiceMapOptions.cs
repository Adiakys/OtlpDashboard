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
/// channel evaluated <i>in priority order</i>. For each <c>kind=Client</c>
/// span the reader picks the first non-null attribute and uses its
/// value as the dependency node's display name. A span never
/// contributes to more than one dependency node, even when several
/// configured keys are present on it — higher-priority keys exclude
/// lower-priority ones at SQL level via <c>IS NULL</c> filters.
/// </para>
///
/// <para>
/// The default order is: <c>service.peer.name</c> (current OTel
/// semconv "named peer" slot), then the kind discriminators
/// (<c>db.system</c>, <c>messaging.system</c>, <c>rpc.system</c>),
/// with the legacy <c>peer.service</c> as the last-resort fallback.
/// Rationale:
/// <list type="bullet">
///   <item>An instrumentation that explicitly emits the modern
///   <c>service.peer.name</c> has chosen a logical peer name —
///   trust it, regardless of any kind attribute on the same span.</item>
///   <item>Otherwise, the kind discriminators give a stable,
///   technology-aware fallback (e.g. <c>postgresql</c>,
///   <c>kafka</c>) that's strictly more useful than what often
///   ends up in <c>peer.service</c>.</item>
///   <item><c>peer.service</c> is last because instrumentation
///   libraries (notably EF Core / Npgsql) frequently fill it with
///   junk like the port number — e.g. a span carrying
///   <c>db.system=postgresql, peer.service=5432</c> would otherwise
///   produce a "5432" dependency node. With the legacy slot last,
///   the kind discriminators win first and the dependency renders
///   as "postgresql".</item>
/// </list>
/// </para>
///
/// <para>
/// Setting <see cref="DependencyAttributes"/> to an empty array
/// disables synthesis entirely. Operators can also reorder the list
/// (or add custom keys) without touching code.
/// </para>
/// </summary>
public sealed class ServiceMapOptions
{
    public const string SectionName = "Dashboard:ServiceMap";

    /// <summary>
    /// OTel attribute keys evaluated in priority order to name a
    /// dependency node. The reader picks the first non-null value
    /// per span. Default order:
    /// <list type="number">
    ///   <item><c>service.peer.name</c> — current OTel semconv
    ///   (≥ 1.36) "named peer" slot. Wins outright when set: an
    ///   instrumentation that emits a logical peer name has
    ///   already chosen the most useful label.</item>
    ///   <item><c>db.system</c> — <c>postgresql</c>, <c>mysql</c>,
    ///   <c>redis</c>, … (OTel semconv).</item>
    ///   <item><c>messaging.system</c> — <c>kafka</c>,
    ///   <c>rabbitmq</c>, …</item>
    ///   <item><c>rpc.system</c> — <c>grpc</c>, …</item>
    ///   <item><c>peer.service</c> — legacy generic peer slot
    ///   (semconv ≤ 1.35); intentionally last because some
    ///   instrumentation libraries (e.g. EF Core / Npgsql) fill it
    ///   with port numbers, which the kind discriminators above
    ///   override with a more meaningful technology name.</item>
    /// </list>
    /// </summary>
    public string[] DependencyAttributes { get; set; } =
    [
        "service.peer.name",
        "db.system",
        "messaging.system",
        "rpc.system",
        "peer.service"
    ];
}
