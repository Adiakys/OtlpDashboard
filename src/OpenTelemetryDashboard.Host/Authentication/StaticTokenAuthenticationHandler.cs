using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetryDashboard.Host.Configuration;

namespace OpenTelemetryDashboard.Host.Authentication;

/// <summary>
/// Authenticates requests by constant-time comparing a presented token
/// against the two configured static tokens (<see cref="DashboardAuthOptions.BrowserToken"/>
/// and <see cref="OtlpAuthOptions.ApiKey"/>). Two header forms are accepted:
/// <list type="bullet">
///   <item><c>Authorization: Bearer &lt;token&gt;</c> — OTel-standard and the
///   only form used by the browser SPA; matches either configured token.</item>
///   <item><c>x-otlp-api-key: &lt;token&gt;</c> — Aspire-compatible OTLP
///   exporter header (e.g. <c>services.Configure&lt;OtlpExporterOptions&gt;(o =&gt;
///   o.Headers = "x-otlp-api-key=...")</c>); matches <b>only</b> the OTLP key.</item>
/// </list>
/// Successful matches are issued a <see cref="ClaimTypes.Role"/> claim of
/// <see cref="RoleBrowser"/> or <see cref="RoleOtlp"/>; the two authorization
/// policies then gate the read-side and OTLP-ingest endpoints respectively.
/// </summary>
public sealed class StaticTokenAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "StaticToken";
    public const string RoleBrowser = "browser";
    public const string RoleOtlp = "otlp";

    private const string BearerPrefix = "Bearer ";
    private const string OtlpApiKeyHeader = "x-otlp-api-key";

    private readonly IOptionsMonitor<DashboardAuthOptions> _tokens;

    public StaticTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IOptionsMonitor<DashboardAuthOptions> tokens)
        : base(options, logger, encoder)
    {
        _tokens = tokens;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var tokens = _tokens.CurrentValue;
        string? role;

        // Preferred: Authorization: Bearer <token>. Matches either token
        // (browser or OTLP) — the role is determined by which one hits.
        if (TryReadBearer(out var bearer))
        {
            if (Matches(bearer, tokens.BrowserToken))
            {
                role = RoleBrowser;
            }
            else if (Matches(bearer, tokens.Otlp.ApiKey))
            {
                role = RoleOtlp;
            }
            else
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid bearer token."));
            }
        }
        // Alternative, OTLP-only: x-otlp-api-key header used by Aspire-style
        // exporter configuration. Never matches the browser token — this
        // header carries OTLP semantics only.
        else if (Request.Headers.TryGetValue(OtlpApiKeyHeader, out var apiKeyHeader))
        {
            var presented = apiKeyHeader.ToString().Trim();
            if (Matches(presented, tokens.Otlp.ApiKey))
            {
                role = RoleOtlp;
            }
            else
            {
                return Task.FromResult(AuthenticateResult.Fail("Invalid OTLP API key."));
            }
        }
        else
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, role)],
            Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(principal, Scheme.Name)));
    }

    private bool TryReadBearer(out string presented)
    {
        presented = string.Empty;
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return false;
        }

        var raw = authHeader.ToString();
        if (!raw.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        presented = raw[BearerPrefix.Length..].Trim();
        return true;
    }

    private static bool Matches(string presented, string? configured)
    {
        if (string.IsNullOrEmpty(configured))
        {
            return false;
        }

        var a = Encoding.UTF8.GetBytes(presented);
        var b = Encoding.UTF8.GetBytes(configured);

        // FixedTimeEquals requires equal-length spans; the length check itself
        // is constant-time equivalent for our purposes (reveals length only,
        // not content — negligible for ≥32-char random tokens).
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
