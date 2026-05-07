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
/// attribute. With the defaults below, a Redis client span tagged
/// <c>peer.service=AuthTokenCache</c> produces an
/// <c>AuthTokenCache</c> dependency; an HTTP client span tagged
/// <c>service.peer.name=billing</c> produces a <c>billing</c>
/// dependency. No service-name whitelist anywhere — the topology is
/// emergent from the data.
/// </para>
///
/// <para>
/// Operators can extend this list to surface custom downstream tiers
/// (e.g. <c>db.system</c>, <c>messaging.system</c>, <c>rpc.system</c>)
/// without touching code; setting it to an empty array disables
/// synthesis entirely.
/// </para>
/// </summary>
public sealed class ServiceMapOptions
{
    public const string SectionName = "Dashboard:ServiceMap";

    /// <summary>
    /// OTel attribute keys whose presence on a <c>kind=Client</c> span
    /// marks it as a call to an external dependency. The value of the
    /// attribute names the dependency node. Defaults track the OTel
    /// semconv "peer service" slot:
    /// <list type="bullet">
    ///   <item><c>peer.service</c> — the legacy key; widely emitted by
    ///   instrumentation libraries that pre-date semconv 1.36.</item>
    ///   <item><c>service.peer.name</c> — the current key (replaces
    ///   <c>peer.service</c>); aligned with the
    ///   <c>service.{name,namespace}</c> resource family.</item>
    /// </list>
    /// Both are listed so the reader keeps working through the
    /// transition, regardless of which SDK / collector version
    /// produced the spans. The dependency-rollup step de-duplicates
    /// by value (e.g. one <c>billing</c> node even when half the
    /// callers emitted the legacy key and the other half the new
    /// one), so listing both is safe and additive.
    ///
    /// To also synthesise dependencies from databases / message
    /// brokers, append <c>db.system</c> and <c>messaging.system</c>
    /// in <c>appsettings.json</c>; they are intentionally not in
    /// the defaults because <c>peer.service</c> values are richer
    /// (a service name, e.g. <c>AuthTokenCache</c>) than the type
    /// (<c>redis</c>).
    /// </summary>
    public string[] DependencyAttributes { get; set; } =
    [
        "peer.service",
        "service.peer.name"
    ];
}
