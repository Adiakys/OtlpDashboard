using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenTelemetryDashboard.Persistence.Converters;
using OpenTelemetryDashboard.Persistence.Metrics.Entities;

namespace OpenTelemetryDashboard.Persistence.Configurations;

public sealed class MetricPointConfiguration : IEntityTypeConfiguration<MetricPointRecord>
{
    public void Configure(EntityTypeBuilder<MetricPointRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("MetricPoints");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();

        builder.Property(p => p.InstrumentId).IsRequired();
        builder.Property(p => p.TimeUnixNano);
        builder.Property(p => p.StartTimeUnixNano);
        builder.Property(p => p.Value);

        builder.Property(p => p.Attributes)
            .HasConversion(new AttributesJsonConverter())
            .Metadata.SetValueComparer(AttributesJsonConverter.Comparer);

        builder.HasOne<InstrumentRecord>()
            .WithMany()
            .HasForeignKey(p => p.InstrumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.TimeUnixNano);
        builder.HasIndex(p => new { p.InstrumentId, p.TimeUnixNano });
    }
}
