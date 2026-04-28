using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Persistence.Converters;

public sealed class SpanIdConverter : ValueConverter<SpanId, byte[]>
{
    public SpanIdConverter()
        : base(id => id.ToByteArray(), bytes => SpanId.FromBytes(bytes))
    {
    }
}
