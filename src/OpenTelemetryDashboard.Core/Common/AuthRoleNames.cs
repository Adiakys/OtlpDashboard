namespace OpenTelemetryDashboard.Core.Common;

/// <summary>
/// Names of the authorization roles minted by the static-token authentication
/// handler. Consumed by both the Host (registering policies) and any module
/// that needs to make role-based decisions (e.g. <c>/info</c>'s redaction
/// gate). Keeping them here avoids module-to-Host backward references.
/// </summary>
public static class AuthRoleNames
{
    /// <summary>Role assigned to clients authenticated via the browser/SPA token.</summary>
    public const string Browser = "browser";

    /// <summary>Role assigned to clients authenticated via the OTLP ingest API key.</summary>
    public const string Otlp = "otlp";
}
