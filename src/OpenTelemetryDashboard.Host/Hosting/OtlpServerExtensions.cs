using Microsoft.AspNetCore.Server.Kestrel.Core;
using OpenTelemetryDashboard.Host.Configuration;

namespace OpenTelemetryDashboard.Host.Hosting;

/// <summary>
/// Wires Kestrel and gRPC for the dual-protocol OTLP listener (gRPC on
/// <c>:4317</c>, HTTP on <c>:4318</c>) and sets a drain-aware shutdown
/// timeout so the background telemetry writer has time to flush.
/// </summary>
internal static class OtlpServerExtensions
{
    public static WebApplicationBuilder AddOtlpServer(this WebApplicationBuilder builder)
    {
        builder.Services.AddIngestionServerOptions(builder.Configuration);

        // The Kestrel/gRPC closures read configuration when they fire (during
        // Build/DI resolution), not when they're handed to the framework. By
        // then ConfigureAppConfiguration overrides from integration tests are
        // already in place — inline reads here would silently bypass them.
        builder.WebHost.ConfigureKestrel((context, options) =>
        {
            var ingestion = ResolveIngestionOptions(context.Configuration);
            options.Limits.MaxRequestBodySize = ingestion.Http.MaxRequestBodySize;
            options.ListenAnyIP(ingestion.Grpc.Port, listen => listen.Protocols = HttpProtocols.Http2);
            options.ListenAnyIP(ingestion.Http.Port, listen => listen.Protocols = HttpProtocols.Http1AndHttp2);
        });

        builder.Services.AddGrpc(grpc =>
        {
            var ingestion = ResolveIngestionOptions(builder.Configuration);
            grpc.MaxReceiveMessageSize = ingestion.Grpc.MaxReceiveMessageSize;
            grpc.EnableDetailedErrors = builder.Environment.IsDevelopment();
        });

        builder.Host.ConfigureHostOptions(o =>
        {
            var ingestion = ResolveIngestionOptions(builder.Configuration);
            // Give the background writer enough time to drain the telemetry channel.
            o.ShutdownTimeout = TimeSpan.FromSeconds(ingestion.Shutdown.DrainTimeoutSeconds + 5);
        });

        return builder;
    }

    private static IngestionServerOptions ResolveIngestionOptions(IConfiguration configuration) =>
        configuration
            .GetSection(IngestionServerOptions.SectionName)
            .Get<IngestionServerOptions>() ?? new IngestionServerOptions();
}
