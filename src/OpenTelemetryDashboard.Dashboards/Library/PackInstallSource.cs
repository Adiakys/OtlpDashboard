using System.Text.Json.Serialization;

namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// How a pack directory landed in the packs path. Drives the "Update"
/// affordance in the SPA — only git-installed packs can be re-pulled
/// in place; filesystem-dropped ones (image layers, manual copies)
/// are managed externally.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PackInstallSource>))]
public enum PackInstallSource
{
    /// <summary>Plain directory dropped into a packs path.</summary>
    Filesystem = 0,

    /// <summary>Cloned from a git repository — has an <c>.install.json</c>
    /// next to the <c>pack.json</c> with the original url/ref/SHA.</summary>
    Git = 1
}
