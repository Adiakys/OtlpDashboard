namespace OpenTelemetryDashboard.Dashboards;

/// <summary>
/// Runtime tuning for the pack subsystem (widget libraries + dashboards
/// shipped together). Bound from <c>Dashboard:Packs</c> in configuration.
/// </summary>
public sealed class PacksOptions
{
    public const string SectionName = "Dashboard:Packs";

    /// <summary>
    /// Ordered list of directories the registry scans for packs. Each
    /// sub-directory at any of these paths must contain a
    /// <c>pack.json</c>. The order matters when two directories expose
    /// packs with the same id — the first occurrence wins, the rest
    /// are skipped with a logged warning. The shipped default lists:
    /// <list type="bullet">
    ///   <item><c>./data/packs</c> — runtime-managed (named volume,
    ///         git installs, drag-and-drop)</item>
    ///   <item><c>./builtin-packs</c> — baked into the image at build
    ///         time, lives in the image layer (no volume shadowing)</item>
    /// </list>
    /// Derived images only need to <c>COPY</c> their packs into the
    /// second path — the configuration above is already in place.
    /// </summary>
    public List<string> Paths { get; set; } = [];

    /// <summary>
    /// Soft cap on the number of packs the registry surfaces, summed
    /// across every scanned path. Beyond this, additional packs are
    /// skipped with a logged warning.
    /// </summary>
    public int MaxPacks { get; set; } = 32;

    /// <summary>
    /// Allow-list of git hosts the install endpoint accepts. Anything
    /// else returns 400 before any network call. Lowercase, no path,
    /// no scheme.
    /// </summary>
    public List<string> AllowedGitHosts { get; set; } = ["github.com", "gitlab.com"];

    /// <summary>
    /// Timeout applied to every git operation (clone, fetch, reset).
    /// The LibGit2Sharp callbacks honour the value to abort runaway
    /// transfers.
    /// </summary>
    public int GitInstallTimeoutSeconds { get; set; } = 60;
}
