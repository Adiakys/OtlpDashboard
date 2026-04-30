namespace OpenTelemetryDashboard.Dashboards.Storage;

/// <summary>
/// Raised when a save is attempted with a stale <c>RowVersion</c>. The HTTP
/// layer translates this to <c>409 Conflict</c>.
/// </summary>
public sealed class WidgetDefinitionConcurrencyException : Exception
{
    public WidgetDefinitionConcurrencyException()
        : base("The widget definition has been modified by another writer. Reload and retry.")
    {
    }

    public WidgetDefinitionConcurrencyException(string message) : base(message)
    {
    }

    public WidgetDefinitionConcurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
