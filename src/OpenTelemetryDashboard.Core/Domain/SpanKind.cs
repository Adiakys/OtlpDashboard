namespace OpenTelemetryDashboard.Core.Domain;

public enum SpanKind
{
    Unspecified = 0,
    Internal = 1,
    Server = 2,
    Client = 3,
    Producer = 4,
    Consumer = 5,
}
