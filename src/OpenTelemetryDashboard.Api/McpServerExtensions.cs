using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetryDashboard.Api.Mcp;

namespace OpenTelemetryDashboard.Api;

/// <summary>
/// Wiring for the read-only MCP server. The Host calls
/// <see cref="AddDashboardMcp"/> + <see cref="MapDashboardMcp"/> only when
/// <c>Dashboard:Mcp:Enabled</c> is true; otherwise no MCP services are
/// registered and no <c>/mcp</c> route is mounted.
/// </summary>
public static class McpServerExtensions
{
    /// <summary>
    /// Default mount path for the MCP endpoint group.
    /// </summary>
    public const string DefaultRoutePrefix = "/mcp";

    /// <summary>
    /// Mirrors the JSON conventions configured for the REST Query API: dictionary
    /// keys are preserved verbatim (so OTel attribute keys like <c>service.name</c>
    /// don't get camelCased) and null-valued properties are emitted explicitly so
    /// clients see a stable shape.
    /// </summary>
    private static readonly JsonSerializerOptions ToolJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DictionaryKeyPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // The MCP SDK marks the options instance read-only on first use; without
        // an explicit type-info resolver that triggers
        // ThrowInvalidOperationException_JsonSerializerOptionsNoTypeInfoResolverSpecified
        // under the trim-friendly defaults of .NET 10. Reflection-based resolution
        // matches the rest of the API (no source generation in use here).
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static IServiceCollection AddDashboardMcp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddMcpServer()
            .WithHttpTransport(o =>
            {
                // Stateless: each request is self-contained, no per-session state
                // is kept between requests. The MCP server only exposes read-only
                // tools — no client-bound notifications, sampling, or elicitation
                // — so stateless is safe and avoids the StatefulSessionManager.
                o.Stateless = true;
            })
            .WithTools<LogTools>(ToolJsonOptions)
            .WithTools<TraceTools>(ToolJsonOptions)
            .WithTools<MetricTools>(ToolJsonOptions);

        return services;
    }

    /// <summary>
    /// Maps the MCP server at <paramref name="pattern"/> (default <c>/mcp</c>).
    /// Returns the endpoint convention builder so callers can chain
    /// <c>RequireAuthorization(...)</c> with the read-API policy.
    /// </summary>
    public static IEndpointConventionBuilder MapDashboardMcp(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultRoutePrefix)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapMcp(pattern);
    }
}
