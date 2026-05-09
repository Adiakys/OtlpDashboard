using Microsoft.AspNetCore.Http;

namespace OpenTelemetryDashboard.Host.Authentication;

/// <summary>
/// Centralises the settings for the SPA's session cookie. The cookie holds
/// the same shared <c>BrowserToken</c> value the SPA used to ship in the
/// <c>Authorization: Bearer</c> header — but as <c>HttpOnly</c>, so any
/// XSS that lands in the dashboard origin can no longer read it via
/// <c>document.cookie</c>.
/// </summary>
internal static class AuthCookie
{
    public const string Name = "oteldash_auth";

    /// <summary>30 minutes, sliding via re-issue at /auth/login.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(30);

    public static void Issue(HttpResponse response, string token, IHostEnvironment env)
    {
        response.Cookies.Append(Name, token, BuildOptions(env));
    }

    public static void Clear(HttpResponse response, IHostEnvironment env)
    {
        // Browsers honour Delete only when the cookie attributes match the
        // ones used at write time, so we hand back the same options shape
        // with an immediate expiry.
        var options = BuildOptions(env);
        options.Expires = DateTimeOffset.UnixEpoch;
        options.MaxAge = TimeSpan.Zero;
        response.Cookies.Append(Name, string.Empty, options);
    }

    private static CookieOptions BuildOptions(IHostEnvironment env) => new()
    {
        HttpOnly = true,
        // Same-origin SPA + API: Strict blocks no legitimate flow and
        // shuts the door on CSRF without any token gymnastics.
        SameSite = SameSiteMode.Strict,
        // In Production we require TLS so the cookie never travels in
        // cleartext. In dev (Development environment) Secure would
        // prevent the cookie from being set on http://localhost — keep
        // it off there.
        Secure = env.IsProduction(),
        Path = "/",
        MaxAge = DefaultLifetime,
        IsEssential = true,
    };
}
