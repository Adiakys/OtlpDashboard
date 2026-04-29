using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Core.Hashing;
using OpenTelemetryDashboard.Persistence.Metrics.Entities;

namespace OpenTelemetryDashboard.Persistence.Configurations;

public sealed class InstrumentConfiguration : IEntityTypeConfiguration<InstrumentRecord>
{
    public void Configure(EntityTypeBuilder<InstrumentRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Instruments");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedOnAdd();

        builder.Property(i => i.ResourceHash)
            .IsRequired()
            .HasMaxLength(ResourceHasher.HashSizeInBytes);

        builder.Property(i => i.ScopeName)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(i => i.ScopeVersion)
            .HasMaxLength(64);

        builder.Property(i => i.Name)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(i => i.Kind).HasConversion<int>();
        builder.Property(i => i.Temporality).HasConversion<int>();

        builder.Property(i => i.Description).HasMaxLength(2_048);
        builder.Property(i => i.Unit).HasMaxLength(64);
        builder.Property(i => i.IsMonotonic);

        builder.HasOne<Resource>()
            .WithMany()
            .HasForeignKey(i => i.ResourceHash)
            .HasPrincipalKey(r => r.Hash)
            .OnDelete(DeleteBehavior.Restrict);

        // Natural key — the (resource, scope, name, kind) tuple uniquely
        // identifies a time-series. Unique index lets the sink lookup or
        // upsert in a single round-trip and prevents duplicate dimension rows.
        builder.HasIndex(i => new { i.ResourceHash, i.ScopeName, i.Name, i.Kind })
            .IsUnique();

        builder.HasIndex(i => i.ResourceHash);
    }
}
