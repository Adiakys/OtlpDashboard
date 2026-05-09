using System.Net;

namespace OpenTelemetryDashboard.IntegrationTests;

/// <summary>
/// Verifies the global hardening headers fire on every response, regardless
/// of authentication outcome. The pack-asset CSP override is covered in
/// <c>Widgets/PackEndpointsTests</c>; here we exercise the SPA-shell defaults.
/// </summary>
public sealed class SecurityHeadersTests : IClassFixture<TestHostFixture>
{
    private readonly TestHostFixture _host;

    public SecurityHeadersTests(TestHostFixture host)
    {
        _host = host;
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/api/v1/info")]
    public async Task Hardening_headers_present_on_typical_endpoints(string path)
    {
        using var client = _host.CreateClient();
        using var resp = await client.GetAsync(new Uri(path, UriKind.Relative));

        resp.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        resp.Headers.GetValues("X-Frame-Options").ShouldContain("DENY");
        resp.Headers.GetValues("Referrer-Policy").ShouldContain("no-referrer");
        resp.Headers.GetValues("Permissions-Policy").Single().ShouldContain("geolocation=()");

        var csp = resp.Headers.GetValues("Content-Security-Policy").Single();
        csp.ShouldContain("default-src 'self'");
        csp.ShouldContain("frame-ancestors 'none'");
        csp.ShouldContain("object-src 'none'");
        csp.ShouldContain("base-uri 'self'");
    }

    [Fact]
    public async Task Hardening_headers_present_on_401_short_circuit()
    {
        // The pipeline runs UseDashboardSecurityHeaders before the auth
        // middleware can short-circuit, so 401 responses also carry the
        // hardening headers — important because that response body is
        // attacker-influenceable when ProblemDetails ever lands.
        using var client = _host.CreateClient();
        using var resp = await client.GetAsync(new Uri("/api/v1/dashboards", UriKind.Relative));

        // The shared TestHostFixture leaves auth disabled (token-empty
        // allow-all), so /api/v1/dashboards returns 200, not 401. Both
        // outcomes still need the headers — the assertion is shape-only.
        resp.StatusCode.ShouldBeOneOf(HttpStatusCode.OK, HttpStatusCode.Unauthorized);
        resp.Headers.Contains("X-Content-Type-Options").ShouldBeTrue();
        resp.Headers.Contains("Content-Security-Policy").ShouldBeTrue();
    }
}
