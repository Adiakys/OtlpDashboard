using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Strips active content from a pack-supplied SVG before it leaves the
/// asset endpoint. The browser CSP override on the endpoint (no script,
/// no fetch, sandbox) is the primary defence; this is the second line
/// for clients that don't enforce CSP and the historical SVG-as-XSS
/// surface (inline <c>&lt;script&gt;</c>, <c>onload="..."</c>,
/// <c>&lt;use href="javascript:..."&gt;</c>, <c>&lt;foreignObject&gt;</c>).
/// </summary>
internal static class SvgSanitizer
{
    private static readonly HashSet<string> DisallowedElements =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "script",
            "foreignObject",
            "iframe",
            "object",
            "embed",
            "audio",
            "video",
            // SVG SMIL animations can mutate any attribute (including href),
            // turning a static SVG into one that points at an attacker URL
            // mid-render. Drop them.
            "animate",
            "animateTransform",
            "animateMotion",
            "set",
        };

    private static readonly XName XlinkHref = XName.Get("href", "http://www.w3.org/1999/xlink");

    public static byte[]? TrySanitize(byte[] svgBytes)
    {
        ArgumentNullException.ThrowIfNull(svgBytes);

        XDocument doc;
        try
        {
            // DTD prohibit — defeats XXE / billion-laughs. Resolver = null —
            // no external entity resolution even if a DOCTYPE somehow slips
            // through. The pack-asset path is anonymous so we treat the
            // input as untrusted by definition.
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersFromEntities = 0,
            };
            using var ms = new MemoryStream(svgBytes, writable: false);
            using var reader = XmlReader.Create(ms, settings);
            doc = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException)
        {
            return null;
        }

        if (doc.Root is null) return null;

        StripDisallowed(doc.Root);

        var output = new MemoryStream();
        var writerSettings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            // SVG icons we ship don't carry an XML declaration; not adding
            // one keeps the sanitised output byte-similar to the input on
            // simple SVGs and avoids surprising browsers that don't expect
            // the prologue inside a <image> tag.
            OmitXmlDeclaration = true,
            Indent = false,
        };
        using (var writer = XmlWriter.Create(output, writerSettings))
        {
            doc.Save(writer);
        }
        return output.ToArray();
    }

    private static void StripDisallowed(XElement element)
    {
        // Two passes per node: collect-then-remove because mutating during
        // enumeration of XContainer.Elements throws.
        var removeElements = new List<XElement>();
        foreach (var child in element.Elements())
        {
            if (DisallowedElements.Contains(child.Name.LocalName))
            {
                removeElements.Add(child);
            }
        }
        foreach (var e in removeElements) e.Remove();

        var removeAttributes = new List<XAttribute>();
        foreach (var attr in element.Attributes())
        {
            if (IsEventHandler(attr.Name.LocalName))
            {
                removeAttributes.Add(attr);
                continue;
            }
            if (IsHrefAttribute(attr.Name) && !IsSafeHref(attr.Value))
            {
                removeAttributes.Add(attr);
            }
        }
        foreach (var a in removeAttributes) a.Remove();

        foreach (var child in element.Elements())
        {
            StripDisallowed(child);
        }
    }

    private static bool IsEventHandler(string localName) =>
        localName.Length > 2
        && (localName[0] == 'o' || localName[0] == 'O')
        && (localName[1] == 'n' || localName[1] == 'N');

    private static bool IsHrefAttribute(XName name) =>
        string.Equals(name.LocalName, "href", StringComparison.OrdinalIgnoreCase)
        || name == XlinkHref;

    private static bool IsSafeHref(string value)
    {
        var trimmed = value.TrimStart();
        // Same-document references and explicit data: images are fine; any
        // scheme-bearing URL (http, https, javascript, vbscript, file, …)
        // gets dropped. SVG icons should never need to fetch anything.
        if (trimmed.StartsWith('#')) return true;
        if (trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return true;
        if (trimmed.Contains(':')) return false;
        // Relative path inside the SVG file — also safe.
        return true;
    }
}
