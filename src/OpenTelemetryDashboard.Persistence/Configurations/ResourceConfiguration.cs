using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Hashing;
using OpenTelemetryDashboard.Persistence.Converters;

namespace OpenTelemetryDashboard.Persistence.Configurations;

public sealed class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Resources");

        builder.HasKey(r => r.Hash);

        builder.Property(r => r.Hash)
            .IsRequired()
            .HasMaxLength(ResourceHasher.HashSizeInBytes);

        builder.Property(r => r.ServiceName)
            .HasMaxLength(256);

        builder.Property(r => r.ServiceInstanceId)
            .HasMaxLength(256);

        builder.Property(r => r.SchemaUrl)
            .HasMaxLength(512);

        builder.Property(r => r.DroppedAttributesCount);

        builder.Property(r => r.Attributes)
            .HasConversion(new AttributesJsonConverter())
            .Metadata.SetValueComparer(AttributesJsonConverter.Comparer);

        builder.HasIndex(r => r.ServiceName);
    }
}
