namespace OpenTelemetryDashboard.Dashboards;

/// <summary>
/// Runtime tuning for the widget library subsystem. Bound from
/// <c>Dashboard:Widgets</c> in configuration.
/// </summary>
public sealed class WidgetsOptions
{
    public const string SectionName = "Dashboard:Widgets";

    /// <summary>
    /// Ordered list of directories the registry scans for widget libraries.
    /// The order matters when two directories expose libraries with the same
    /// <c>manifest.id</c> — the first occurrence wins, the rest are skipped
    /// with a logged warning. The shipped default lists both:
    /// <list type="bullet">
    ///   <item><c>./data/widget-libraries</c> — runtime-managed (named
    ///         volume, git installs, drag-and-drop)</item>
    ///   <item><c>./builtin-libraries</c> — baked into the image at build
    ///         time, lives in the image layer (no volume shadowing)</item>
    /// </list>
    /// Derived images only need to <c>COPY</c> their libraries into the
    /// second path — the configuration above is already in place.
    /// </summary>
    public List<string> LibrariesPaths { get; set; } = [];

    /// <summary>
    /// Soft cap on the number of libraries the registry surfaces, summed
    /// across every scanned path. Beyond this, additional libraries are
    /// skipped with a logged warning.
    /// </summary>
    public int MaxLibraries { get; set; } = 32;
}
