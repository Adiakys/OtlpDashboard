namespace OpenTelemetryDashboard.Dashboards;

/// <summary>
/// Runtime tuning for the widget library subsystem. Bound from
/// <c>Dashboard:Widgets</c> in configuration. All knobs are optional —
/// <see cref="LibrariesPath"/> falls back to a content-root-relative path
/// resolved at startup when left null.
/// </summary>
public sealed class WidgetsOptions
{
    public const string SectionName = "Dashboard:Widgets";

    /// <summary>
    /// Absolute path to the widget libraries root. Each immediate
    /// subdirectory is treated as a library iff it contains a valid
    /// <c>manifest.json</c>. When null, defaults to
    /// <c>&lt;ContentRoot&gt;/widget-libraries</c> at startup.
    /// </summary>
    public string? LibrariesPath { get; set; }

    /// <summary>
    /// Soft cap on the number of libraries the registry will surface. Beyond
    /// this, additional libraries are skipped with a logged warning.
    /// </summary>
    public int MaxLibraries { get; set; } = 32;
}
