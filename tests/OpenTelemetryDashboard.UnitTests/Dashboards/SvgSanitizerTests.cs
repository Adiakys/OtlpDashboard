using System.Reflection;
using System.Text;

namespace OpenTelemetryDashboard.UnitTests.Dashboards;

public class SvgSanitizerTests
{
    // SvgSanitizer is internal — reach it via reflection rather than
    // widening visibility just for tests.
    private static readonly MethodInfo TrySanitize = typeof(OpenTelemetryDashboard.Dashboards.DashboardsServiceCollectionExtensions)
        .Assembly
        .GetType("OpenTelemetryDashboard.Dashboards.Library.SvgSanitizer", throwOnError: true)!
        .GetMethod("TrySanitize", BindingFlags.Public | BindingFlags.Static)!;

    private static string? Sanitize(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var result = (byte[]?)TrySanitize.Invoke(null, [bytes]);
        return result is null ? null : Encoding.UTF8.GetString(result);
    }

    [Fact]
    public void Strips_script_element()
    {
        var output = Sanitize(
            """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script><circle/></svg>""");

        output.ShouldNotBeNull();
        output.ShouldNotContain("script", Case.Insensitive);
        output.ShouldContain("<circle");
    }

    [Fact]
    public void Strips_foreignObject_element()
    {
        var output = Sanitize(
            """<svg xmlns="http://www.w3.org/2000/svg"><foreignObject><div>x</div></foreignObject></svg>""");

        output.ShouldNotBeNull();
        output.ShouldNotContain("foreignObject", Case.Insensitive);
    }

    [Theory]
    [InlineData("animate")]
    [InlineData("animateTransform")]
    [InlineData("animateMotion")]
    [InlineData("set")]
    public void Strips_smil_animations(string element)
    {
        var output = Sanitize(
            $"""<svg xmlns="http://www.w3.org/2000/svg"><{element} attributeName="href" to="javascript:alert(1)"/></svg>""");

        output.ShouldNotBeNull();
        output.ShouldNotContain($"<{element}", Case.Insensitive);
    }

    [Fact]
    public void Strips_event_handler_attributes()
    {
        var output = Sanitize(
            """<svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)" onclick="alert(2)"><circle/></svg>""");

        output.ShouldNotBeNull();
        output.ShouldNotContain("onload", Case.Insensitive);
        output.ShouldNotContain("onclick", Case.Insensitive);
    }

    [Fact]
    public void Strips_javascript_href()
    {
        var output = Sanitize(
            """<svg xmlns="http://www.w3.org/2000/svg"><a href="javascript:alert(1)"><circle/></a></svg>""");

        output.ShouldNotBeNull();
        output.ShouldNotContain("javascript:", Case.Insensitive);
    }

    [Fact]
    public void Strips_javascript_xlink_href()
    {
        var output = Sanitize(
            """<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"><use xlink:href="javascript:alert(1)"/></svg>""");

        output.ShouldNotBeNull();
        output.ShouldNotContain("javascript:", Case.Insensitive);
    }

    [Fact]
    public void Preserves_same_document_href()
    {
        var output = Sanitize(
            """<svg xmlns="http://www.w3.org/2000/svg"><a href="#group"><circle/></a></svg>""");

        output.ShouldNotBeNull();
        output.ShouldContain("#group");
    }

    [Fact]
    public void Preserves_relative_href()
    {
        var output = Sanitize(
            """<svg xmlns="http://www.w3.org/2000/svg"><a href="other.svg"><circle/></a></svg>""");

        output.ShouldNotBeNull();
        output.ShouldContain("other.svg");
    }

    [Fact]
    public void Strips_external_http_href()
    {
        // SVG icons should never need to fetch external resources; any
        // scheme-bearing URL is dropped, not just javascript: ones.
        var output = Sanitize(
            """<svg xmlns="http://www.w3.org/2000/svg"><image href="https://attacker/leak.png"/></svg>""");

        output.ShouldNotBeNull();
        output.ShouldNotContain("attacker");
    }

    [Fact]
    public void Returns_null_on_malformed_svg()
    {
        var output = Sanitize("<svg><unclosed");

        output.ShouldBeNull();
    }

    [Fact]
    public void Rejects_doctype_with_external_entity()
    {
        // XXE / billion laughs — the DTD is prohibited at parse time, so
        // the document is rejected rather than expanded.
        var output = Sanitize(
            """
            <?xml version="1.0"?>
            <!DOCTYPE foo [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <svg xmlns="http://www.w3.org/2000/svg"><text>&xxe;</text></svg>
            """);

        output.ShouldBeNull();
    }
}
