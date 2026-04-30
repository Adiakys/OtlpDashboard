namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Base type for install failures. Sub-types map to specific HTTP status
/// codes in the endpoint layer; callers can also catch this base when
/// they only care that the install pipeline rolled back cleanly.
/// </summary>
public abstract class WidgetLibraryInstallException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// The install URL is malformed, uses a non-https scheme, or its host is
/// not in <c>WidgetsOptions.AllowedGitHosts</c>. Maps to HTTP 400.
/// </summary>
public sealed class WidgetLibraryHostNotAllowedException(string host)
    : WidgetLibraryInstallException(
        $"Host '{host}' is not in the configured AllowedGitHosts list.")
{
    public string Host { get; } = host;
}

/// <summary>
/// The cloned repo's <c>manifest.json</c> is missing or doesn't satisfy
/// the manifest parser's contract (id mismatch, regex violation, missing
/// required fields). Maps to HTTP 422.
/// </summary>
public sealed class WidgetLibraryManifestInvalidException(string detail)
    : WidgetLibraryInstallException($"manifest.json is not valid: {detail}")
{
    public string Detail { get; } = detail;
}

/// <summary>
/// A library with the same id is already installed. Maps to HTTP 409.
/// The user must uninstall the existing one before installing a fresh
/// copy.
/// </summary>
public sealed class WidgetLibraryIdCollisionException(string libraryId)
    : WidgetLibraryInstallException(
        $"A library with id '{libraryId}' is already installed.")
{
    public string LibraryId { get; } = libraryId;
}

/// <summary>
/// Update was requested for a library that wasn't installed via git
/// (no <c>.install.json</c> with <c>source: "git"</c>). Maps to HTTP 400.
/// </summary>
public sealed class WidgetLibraryNotGitInstalledException(string libraryId)
    : WidgetLibraryInstallException(
        $"Library '{libraryId}' was not installed from git and cannot be updated through the API.")
{
    public string LibraryId { get; } = libraryId;
}

/// <summary>
/// Wraps any other unexpected failure during clone/fetch/reset (network,
/// timeout, libgit2 error). Maps to HTTP 500.
/// </summary>
public sealed class WidgetLibraryGitOperationException(string message, Exception inner)
    : WidgetLibraryInstallException(message, inner);
