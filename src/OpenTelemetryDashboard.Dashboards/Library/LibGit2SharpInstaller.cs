using LibGit2Sharp;
using Microsoft.Extensions.Logging;

namespace OpenTelemetryDashboard.Dashboards.Library;

/// <summary>
/// Default <see cref="IGitInstaller"/> backed by LibGit2Sharp. Native
/// libgit2 binaries are bundled by the package — no `git` executable
/// required in the container.
/// </summary>
public sealed class LibGit2SharpInstaller : IGitInstaller
{
    private readonly ILogger<LibGit2SharpInstaller> _logger;

    public LibGit2SharpInstaller(ILogger<LibGit2SharpInstaller> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task CloneAsync(
        string url,
        string gitRef,
        string targetDir,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDir);

        // LibGit2Sharp.Clone is synchronous; offload to the threadpool so
        // the request thread can serve other traffic while we wait.
        await Task.Run(() => Clone(url, gitRef, targetDir, timeout, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task FetchAndResetAsync(
        string repoDir,
        string gitRef,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoDir);
        ArgumentException.ThrowIfNullOrWhiteSpace(gitRef);

        await Task.Run(() => FetchAndReset(repoDir, gitRef, timeout, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public string ResolveHead(string repoDir)
    {
        using var repo = new Repository(repoDir);
        return repo.Head.Tip.Sha;
    }

    private static void Clone(string url, string gitRef, string targetDir, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        var options = new CloneOptions
        {
            // Shallow clone — we only need the working tree at <ref>, never history.
            FetchOptions =
            {
                Depth = 1,
                OnTransferProgress = progress =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return DateTime.UtcNow <= deadline;
                }
            },
            // Submodules disabled by design; widget packs are flat directories.
            RecurseSubmodules = false
        };

        // Branch only accepts branch names. Tags and SHAs are resolved by a
        // post-clone checkout.
        if (LooksLikeBranch(gitRef))
        {
            options.BranchName = gitRef;
        }

        Repository.Clone(url, targetDir, options);

        // Tag or SHA: do a detached checkout to the requested ref.
        if (!LooksLikeBranch(gitRef))
        {
            using var repo = new Repository(targetDir);
            CheckoutRef(repo, gitRef);
        }
    }

    private static void FetchAndReset(string repoDir, string gitRef, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        using var repo = new Repository(repoDir);

        var fetchOptions = new FetchOptions
        {
            OnTransferProgress = progress =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return DateTime.UtcNow <= deadline;
            }
        };
        var remote = repo.Network.Remotes["origin"]
            ?? throw new InvalidOperationException("Repository has no 'origin' remote.");
        var refspecs = remote.FetchRefSpecs.Select(r => r.Specification);
        Commands.Fetch(repo, remote.Name, refspecs, fetchOptions, logMessage: null);

        CheckoutRef(repo, gitRef);
    }

    private static void CheckoutRef(Repository repo, string gitRef)
    {
        // Try in order: explicit local ref, remote-tracking branch, tag, SHA.
        // The first hit wins; LibGit2Sharp's `Lookup` returns null instead of
        // throwing when the ref isn't found, so we cascade.
        var commit =
            repo.Lookup<Commit>(gitRef) ??
            repo.Branches[$"origin/{gitRef}"]?.Tip ??
            repo.Tags[gitRef]?.Target as Commit ??
            throw new InvalidOperationException($"Ref '{gitRef}' not found in repository.");

        repo.Reset(ResetMode.Hard, commit);
    }

    private static bool LooksLikeBranch(string gitRef)
    {
        // SHAs are hex strings of length 7-40; tags conventionally start with
        // 'v' and contain a digit. Anything else (e.g. "main", "develop") is
        // treated as a branch — `Repository.Clone --branch` handles both
        // branches and tags, but the heuristic is forgiving on edge cases.
        if (gitRef.Length is >= 7 and <= 40 && gitRef.All(IsHex)) return false;
        return true;
    }

    private static bool IsHex(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
