using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Hashing;
using OpenTelemetryDashboard.Persistence.Converters;

namespace OpenTelemetryDashboard.Persistence.Configurations;

public sealed class LogRecordConfiguration : IEntityTypeConfiguration<LogRecord>
{
    public void Configure(EntityTypeBuilder<LogRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("LogRecords");

        builder.Property<long>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");

        builder.Property(l => l.ResourceHash)
            .IsRequired()
            .HasMaxLength(ResourceHasher.HashSizeInBytes);

        builder.Property(l => l.TimeUnixNano);
        builder.Property(l => l.ObservedTimeUnixNano);

        builder.Property(l => l.SeverityNumber).HasConversion<int>();
        builder.Property(l => l.SeverityText).HasMaxLength(64);
        builder.Property(l => l.Body).HasMaxLength(8_192);

        builder.Property(l => l.TraceId)
            .HasConversion(new TraceIdConverter())
            .HasMaxLength(TraceId.SizeInBytes);

        builder.Property(l => l.SpanId)
            .HasConversion(new SpanIdConverter())
            .HasMaxLength(SpanId.SizeInBytes);

        builder.Property(l => l.Flags);
        builder.Property(l => l.ScopeName).HasMaxLength(256);
        builder.Property(l => l.ScopeVersion).HasMaxLength(64);

        builder.Property(l => l.Attributes)
            .HasConversion(new AttributesJsonConverter())
            .Metadata.SetValueComparer(AttributesJsonConverter.Comparer);

        builder.Property(l => l.DroppedAttributesCount);

        builder.HasOne<Resource>()
            .WithMany()
            .HasForeignKey(l => l.ResourceHash)
            .HasPrincipalKey(r => r.Hash)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(l => l.TimeUnixNano);
        builder.HasIndex(l => l.SeverityNumber);
        builder.HasIndex(l => l.TraceId);
        builder.HasIndex(l => l.ResourceHash);
    }
}
