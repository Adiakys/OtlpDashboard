namespace OpenTelemetryDashboard.Dashboards.Contracts;

/// <summary>
/// Wire payload for <c>POST /api/v1/widgets/libraries/install</c>. Both
/// fields are required: <see cref="Url"/> must be an https URL whose host
/// is in <c>WidgetsOptions.AllowedGitHosts</c>, and <see cref="Ref"/>
/// pins the checkout to a tag, branch, or commit SHA. The server resolves
/// the ref to an exact SHA at install time and stores it as
/// <c>refResolved</c> in <c>.install.json</c> for audit and update.
/// </summary>
public sealed record InstallLibraryRequest(string Url, string Ref);
