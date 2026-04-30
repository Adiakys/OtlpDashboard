using System.Text.Json.Serialization;

namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// How a library's directory landed in the libraries path. Drives the
/// "Update" affordance in the SPA — only git-installed libraries can be
/// re-pulled in place; filesystem-dropped ones are managed externally.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<LibraryInstallSource>))]
public enum LibraryInstallSource
{
    /// <summary>Plain directory dropped into the libraries path.</summary>
    Filesystem = 0,

    /// <summary>Cloned from a git repository — has an <c>.install.json</c> next
    /// to the manifest with the original url/ref/SHA.</summary>
    Git = 1
}
