using Microsoft.Extensions.Logging;

namespace OpenTelemetryDashboard.Host.Authentication;

internal static partial class AuthStartupLog
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "DASHBOARD__BROWSERTOKEN is not set — the read-side Query API is publicly accessible.")]
    public static partial void BrowserTokenNotSet(this ILogger logger);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "DASHBOARD__OTLP__APIKEY is not set — OTLP ingestion (HTTP + gRPC) accepts unauthenticated clients.")]
    public static partial void OtlpApiKeyNotSet(this ILogger logger);
}
