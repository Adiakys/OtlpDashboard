namespace OpenTelemetryDashboard.Dashboards.Contracts;

/// <summary>
/// Wire payload for <c>POST /api/v1/packs/install</c>.
/// <see cref="Url"/> must be an https URL whose host is in
/// <c>PacksOptions.AllowedGitHosts</c>; <see cref="Ref"/> pins the
/// checkout to a tag, branch, or commit SHA. <see cref="Path"/> is
/// optional: when set, the installer expects <c>pack.json</c> at
/// <c>&lt;clone&gt;/&lt;path&gt;</c> instead of the clone root —
/// useful for a monorepo of packs where each is a sub-directory.
/// </summary>
public sealed record InstallPackRequest(string Url, string Ref, string? Path = null);
