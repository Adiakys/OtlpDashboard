using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Persistence.Converters;

public sealed class TraceIdConverter : ValueConverter<TraceId, byte[]>
{
    public TraceIdConverter()
        : base(id => id.ToByteArray(), bytes => TraceId.FromBytes(bytes))
    {
    }
}
