using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Host.Configuration;

namespace OpenTelemetryDashboard.Host.Authentication;

/// <summary>
/// Login / logout endpoints that swap the SPA's password for an HttpOnly
/// cookie. The cookie carries the same shared BrowserToken the SPA used
/// to send as a bearer header — the only architectural change is "JS no
/// longer touches the value". Bearer-via-Authorization keeps working for
/// headless integrations.
/// </summary>
internal static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuth(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync)
            .WithName("AuthLogin")
            .AllowAnonymous();
        group.MapPost("/logout", Logout)
            .WithName("AuthLogout")
            .AllowAnonymous();

        return group;
    }

    public sealed record LoginRequest(string? Token);

    private static Task<Results<NoContent, UnauthorizedHttpResult>> LoginAsync(
        LoginRequest request,
        HttpContext context,
        IOptionsMonitor<DashboardAuthOptions> tokens,
        IHostEnvironment env)
    {
        var presented = request?.Token ?? string.Empty;
        var configured = tokens.CurrentValue.BrowserToken;

        // Auth is opt-in: when no BrowserToken is configured, /login refuses
        // to mint a cookie. The rest of the API is allow-all in that mode
        // (see AuthServiceCollectionExtensions); the SPA simply skips the
        // login flow because the read-API never returns 401.
        if (string.IsNullOrEmpty(configured))
        {
            return Task.FromResult<Results<NoContent, UnauthorizedHttpResult>>(TypedResults.Unauthorized());
        }

        if (!ConstantTimeEquals(presented, configured))
        {
            return Task.FromResult<Results<NoContent, UnauthorizedHttpResult>>(TypedResults.Unauthorized());
        }

        AuthCookie.Issue(context.Response, presented, env);
        return Task.FromResult<Results<NoContent, UnauthorizedHttpResult>>(TypedResults.NoContent());
    }

    private static NoContent Logout(HttpContext context, IHostEnvironment env)
    {
        AuthCookie.Clear(context.Response, env);
        return TypedResults.NoContent();
    }

    private static bool ConstantTimeEquals(string presented, string configured)
    {
        var a = Encoding.UTF8.GetBytes(presented);
        var b = Encoding.UTF8.GetBytes(configured);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
