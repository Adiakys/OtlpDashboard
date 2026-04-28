namespace OpenTelemetryDashboard.Dashboards.Storage;

/// <summary>
/// Raised when a save is attempted with a stale <c>RowVersion</c>. The HTTP
/// layer translates this to <c>409 Conflict</c>.
/// </summary>
public sealed class DashboardConcurrencyException : Exception
{
    public DashboardConcurrencyException()
        : base("The dashboard has been modified by another writer. Reload and retry.")
    {
    }

    public DashboardConcurrencyException(string message) : base(message)
    {
    }

    public DashboardConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
