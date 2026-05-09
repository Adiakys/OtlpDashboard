namespace OpenTelemetryDashboard.Ingestion.Translators;

/// <summary>
/// Hard caps on the shape of an OTLP payload as it flows through the
/// translators. A wire-size limit (Kestrel/gRPC max body) caps a single
/// request to ~16MB but says nothing about how that 16MB is structured —
/// a hostile producer can encode hundreds of thousands of attributes per
/// span, deeply nested kvlists, or a single multi-megabyte string. These
/// constants stop the translator before recursive descent blows the stack
/// or before a single span/log inflates the in-memory representation past
/// what the EF sinks can handle.
/// </summary>
/// <remarks>
/// Values picked to be generous for legitimate producers and tight enough
/// to keep a worst-case batch bounded:
/// <list type="bullet">
///   <item>OTel spec recommends ≤128 attributes per span/log/event/link.</item>
///   <item>Span events/links typically &lt;10; runaway counts are pathologic.</item>
///   <item>Deeply nested kvlists (&gt;8 levels) are not produced by any SDK we know of.</item>
///   <item>16KB string is enough for stack traces / formatted log lines.</item>
/// </list>
/// </remarks>
internal static class OtlpTranslationLimits
{
    public const int MaxAttributeDepth = 8;
    public const int MaxAttributeCollectionSize = 256;
    public const int MaxAttributeStringLength = 16 * 1024;
    public const int MaxAttributesPerEntity = 128;
    public const int MaxEventsPerSpan = 128;
    public const int MaxLinksPerSpan = 128;
    public const int MaxLogBodyLength = 16 * 1024;

    public const string TruncationSuffix = "…[truncated]";
}
