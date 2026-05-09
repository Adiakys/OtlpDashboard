namespace OpenTelemetryDashboard.Host.Hosting;

/// <summary>
/// Sets the standard hardening headers on every response: a baseline CSP for
/// the SPA shell, MIME sniffing off, click-jacking off, no referrer leaks
/// to third parties, and HSTS once the request arrived over HTTPS. The
/// pack-assets endpoint clears the SPA-shell CSP and substitutes its own
/// stricter one — see <c>PackEndpoints.GetPackAssetAsync</c>.
/// </summary>
internal static class SecurityHeadersExtensions
{
    // Why 'unsafe-inline' on script-src and style-src:
    //   - The Nuxt color-mode bootstrap is an inline <script> in index.html
    //     that flips the data-color-mode-forced class before paint. It runs
    //     synchronously so the page doesn't flash the wrong theme.
    //   - The __NUXT__ runtime config is also an inline <script> with the
    //     buildId, locale list, etc.
    //   - Nuxt UI / Tailwind utility classes injected at runtime use inline
    //     style attributes for transitions.
    // The SPA is static, so we can't generate a per-request nonce; hashing
    // the two scripts breaks every time the buildId changes. The remaining
    // mitigations (nosniff, frame-ancestors none, base-uri self, object-src
    // none, the redacted /info endpoint, the SVG-asset CSP override) keep
    // the XSS blast radius bounded.
    private const string ContentSecurityPolicy =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self' data:; " +
        "connect-src 'self'; " +
        "object-src 'none'; " +
        "base-uri 'self'; " +
        "frame-ancestors 'none'; " +
        "form-action 'self';";

    public static IApplicationBuilder UseDashboardSecurityHeaders(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            // OnStarting fires just before the response bytes are flushed,
            // so the headers are present whatever wrote the response —
            // including the global exception handler, which calls
            // Response.Clear() on its way to writing a ProblemDetails 500
            // and would otherwise drop headers set on the request way in.
            context.Response.OnStarting(static state =>
            {
                var ctx = (HttpContext)state;
                var headers = ctx.Response.Headers;

                // Endpoints that override the SPA-shell CSP (e.g. PackEndpoints
                // for SVG assets) set their own value first; we don't clobber it.
                if (!headers.ContainsKey("Content-Security-Policy"))
                {
                    headers["Content-Security-Policy"] = ContentSecurityPolicy;
                }
                headers["X-Content-Type-Options"] = "nosniff";
                headers["X-Frame-Options"] = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = "geolocation=(), camera=(), microphone=(), payment=()";

                // HSTS only when we're confident the connection is TLS — the
                // dashboard is typically deployed behind a reverse proxy that
                // terminates TLS, so we trust UseForwardedHeaders (when wired)
                // to surface the original scheme. Without that, IsHttps reads
                // Kestrel's local socket scheme, which is fine for direct TLS.
                if (ctx.Request.IsHttps)
                {
                    headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
                }

                return Task.CompletedTask;
            }, context);

            await next().ConfigureAwait(false);
        });
    }
}
