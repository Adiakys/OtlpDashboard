namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Thrown when an uninstall is requested for a library id that isn't
/// currently in the registry. Distinct from "registry empty" so the
/// endpoint can map to 404 cleanly.
/// </summary>
public sealed class WidgetLibraryNotFoundException(string libraryId)
    : Exception($"Widget library '{libraryId}' is not installed.")
{
    public string LibraryId { get; } = libraryId;
}

/// <summary>
/// Thrown when an uninstall is requested for a library that lives outside
/// the first configured (runtime-managed) path — typically a baked-in
/// library shipped via an image layer. The library can only be removed by
/// rebuilding the image without it.
/// </summary>
public sealed class WidgetLibraryNotRemovableException(string libraryId)
    : Exception(
        $"Widget library '{libraryId}' lives in a read-only path and cannot be uninstalled at runtime.")
{
    public string LibraryId { get; } = libraryId;
}
