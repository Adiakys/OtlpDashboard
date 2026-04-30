namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Minimal port for the git operations the install service needs. Exists
/// so unit tests can supply a fake that writes the filesystem directly
/// without touching the network or the libgit2 native bits.
/// </summary>
public interface IGitInstaller
{
    /// <summary>
    /// Shallow-clone <paramref name="url"/> at <paramref name="ref"/> into
    /// <paramref name="targetDir"/>. The directory must not exist beforehand
    /// (the implementation creates it). Throws on any libgit2 error.
    /// </summary>
    Task CloneAsync(
        string url,
        string gitRef,
        string targetDir,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetch from origin and reset the working tree to the given
    /// <paramref name="ref"/> (tag, branch, or SHA). Used by the update
    /// pipeline. Discards local modifications.
    /// </summary>
    Task FetchAndResetAsync(
        string repoDir,
        string gitRef,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the SHA of HEAD as a 40-char hex string. Called right after
    /// clone/update so the install service can pin the resolved commit in
    /// <c>.install.json</c>.
    /// </summary>
    string ResolveHead(string repoDir);
}
