using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Hashing;
using OpenTelemetryDashboard.Persistence.Converters;

namespace OpenTelemetryDashboard.Persistence.Configurations;

public sealed class SpanConfiguration : IEntityTypeConfiguration<Span>
{
    public void Configure(EntityTypeBuilder<Span> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Spans");

        builder.Property<long>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");

        builder.Property(s => s.TraceId)
            .HasConversion(new TraceIdConverter())
            .HasMaxLength(TraceId.SizeInBytes)
            .IsRequired();

        builder.Property(s => s.SpanId)
            .HasConversion(new SpanIdConverter())
            .HasMaxLength(SpanId.SizeInBytes)
            .IsRequired();

        builder.Property(s => s.ParentSpanId)
            .HasConversion(new NullableSpanIdConverter())
            .HasMaxLength(SpanId.SizeInBytes);

        builder.Property(s => s.ResourceHash)
            .IsRequired()
            .HasMaxLength(ResourceHasher.HashSizeInBytes);

        builder.Property(s => s.Name)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(s => s.Kind).HasConversion<int>();
        builder.Property(s => s.StatusCode).HasConversion<int>();

        builder.Property(s => s.StatusMessage).HasMaxLength(1024);
        builder.Property(s => s.ScopeName).HasMaxLength(256);
        builder.Property(s => s.ScopeVersion).HasMaxLength(64);
        builder.Property(s => s.StartUnixNano);
        builder.Property(s => s.EndUnixNano);
        builder.Property(s => s.Flags);

        builder.Property(s => s.Attributes)
            .HasConversion(new AttributesJsonConverter())
            .Metadata.SetValueComparer(AttributesJsonConverter.Comparer);

        builder.Property(s => s.DroppedAttributesCount);
        builder.Property(s => s.DroppedEventsCount);
        builder.Property(s => s.DroppedLinksCount);

        builder.HasOne<Resource>()
            .WithMany()
            .HasForeignKey(s => s.ResourceHash)
            .HasPrincipalKey(r => r.Hash)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => s.TraceId);
        builder.HasIndex(s => s.StartUnixNano);
        builder.HasIndex(s => s.ResourceHash);

        builder.OwnsMany(s => s.Events, events =>
        {
            events.ToTable("SpanEvents");
            events.WithOwner().HasForeignKey("OwnerSpanId");
            events.Property<long>("Id").ValueGeneratedOnAdd();
            events.HasKey("Id");
            events.Property(e => e.Name).IsRequired().HasMaxLength(512);
            events.Property(e => e.TimeUnixNano);
            events.Property(e => e.DroppedAttributesCount);
            events.Property(e => e.Attributes)
                .HasConversion(new AttributesJsonConverter())
                .Metadata.SetValueComparer(AttributesJsonConverter.Comparer);
        });

        builder.OwnsMany(s => s.Links, links =>
        {
            links.ToTable("SpanLinks");
            links.WithOwner().HasForeignKey("OwnerSpanId");
            links.Property<long>("Id").ValueGeneratedOnAdd();
            links.HasKey("Id");
            links.Property(l => l.TraceId)
                .HasConversion(new TraceIdConverter())
                .HasMaxLength(TraceId.SizeInBytes)
                .IsRequired()
                .HasColumnName("LinkedTraceId");
            links.Property(l => l.SpanId)
                .HasConversion(new SpanIdConverter())
                .HasMaxLength(SpanId.SizeInBytes)
                .IsRequired()
                .HasColumnName("LinkedSpanId");
            links.Property(l => l.Flags);
            links.Property(l => l.DroppedAttributesCount);
            links.Property(l => l.Attributes)
                .HasConversion(new AttributesJsonConverter())
                .Metadata.SetValueComparer(AttributesJsonConverter.Comparer);
        });
    }
}
