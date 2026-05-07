namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Thrown when an uninstall / update is requested for a pack id that
/// isn't currently in the registry. Distinct from "registry empty" so
/// the endpoint can map to 404 cleanly.
/// </summary>
public sealed class PackNotFoundException(string packId)
    : Exception($"Pack '{packId}' is not installed.")
{
    public string PackId { get; } = packId;
}

/// <summary>
/// Thrown when an uninstall is requested for a pack that lives outside
/// the first configured (runtime-managed) path — typically a baked-in
/// pack shipped via an image layer. The pack can only be removed by
/// rebuilding the image without it.
/// </summary>
public sealed class PackNotRemovableException(string packId)
    : Exception(
        $"Pack '{packId}' lives in a read-only path and cannot be uninstalled at runtime.")
{
    public string PackId { get; } = packId;
}

/// <summary>
/// Base type for install failures. Sub-types map to specific HTTP
/// status codes in the endpoint layer; callers can also catch this
/// base when they only care that the install pipeline rolled back
/// cleanly.
/// </summary>
public abstract class PackInstallException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// The install URL is malformed, uses a non-https scheme, or its host
/// is not in <c>PacksOptions.AllowedGitHosts</c>. Maps to HTTP 400.
/// </summary>
public sealed class PackHostNotAllowedException(string host)
    : PackInstallException(
        $"Host '{host}' is not in the configured AllowedGitHosts list.")
{
    public string Host { get; } = host;
}

/// <summary>
/// The cloned repo's <c>pack.json</c> is missing or invalid (id
/// mismatch, regex violation, library/dashboard reference outside
/// the pack root). Maps to HTTP 422.
/// </summary>
public sealed class PackManifestInvalidException(string detail)
    : PackInstallException($"pack.json is not valid: {detail}")
{
    public string Detail { get; } = detail;
}

/// <summary>
/// A pack with the same id is already installed. Maps to HTTP 409.
/// The user must uninstall the existing one before installing a fresh
/// copy.
/// </summary>
public sealed class PackIdCollisionException(string packId)
    : PackInstallException(
        $"A pack with id '{packId}' is already installed.")
{
    public string PackId { get; } = packId;
}

/// <summary>
/// Update was requested for a pack that wasn't installed via git (no
/// <c>.install.json</c> with <c>source: "git"</c>). Maps to HTTP 400.
/// </summary>
public sealed class PackNotGitInstalledException(string packId)
    : PackInstallException(
        $"Pack '{packId}' was not installed from git and cannot be updated through the API.")
{
    public string PackId { get; } = packId;
}

/// <summary>
/// Install <see cref="InstallPathInvalid"/>: the optional sub-path
/// argument doesn't pass the relative-path safety check or doesn't
/// exist after the clone. Maps to HTTP 400.
/// </summary>
public sealed class PackInstallPathInvalidException(string detail)
    : PackInstallException($"Install path is not valid: {detail}")
{
    public string Detail { get; } = detail;
}

/// <summary>
/// Wraps any other unexpected failure during clone/fetch/reset (network,
/// timeout, libgit2 error). Maps to HTTP 500.
/// </summary>
public sealed class PackGitOperationException(string message, Exception inner)
    : PackInstallException(message, inner);
