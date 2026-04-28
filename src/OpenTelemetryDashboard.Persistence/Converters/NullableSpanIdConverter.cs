using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenTelemetryDashboard.Core.Domain;

namespace OpenTelemetryDashboard.Persistence.Converters;

public sealed class NullableSpanIdConverter : ValueConverter<SpanId?, byte[]?>
{
    public NullableSpanIdConverter()
        : base(
            id => id.HasValue ? id.Value.ToByteArray() : null,
            bytes => bytes != null && bytes.Length == SpanId.SizeInBytes
                ? SpanId.FromBytes(bytes)
                : null)
    {
    }
}
