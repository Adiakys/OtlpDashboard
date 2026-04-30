using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenTelemetryDashboard.Dashboards.Domain;

namespace OpenTelemetryDashboard.Persistence.Configurations;

public sealed class WidgetDefinitionConfiguration : IEntityTypeConfiguration<WidgetDefinition>
{
    public void Configure(EntityTypeBuilder<WidgetDefinition> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("WidgetDefinitions");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.Name)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(d => d.Description)
            .HasMaxLength(280);

        builder.Property(d => d.Icon)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(d => d.Engine)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(d => d.BaseKind)
            .HasMaxLength(64);

        // Opaque JSON payloads owned by the SPA. Stored unbounded text — the
        // validation layer enforces size caps so providers don't need
        // column-size juggling.
        builder.Property(d => d.ConfigJson)
            .IsRequired();

        builder.Property(d => d.SpecJson);

        builder.Property(d => d.DefaultW);
        builder.Property(d => d.DefaultH);

        builder.Property(d => d.UpdatedAt);

        // App-managed optimistic concurrency token, same pattern as Dashboard.
        builder.Property(d => d.RowVersion)
            .IsConcurrencyToken();

        // Listing the user's saved widgets sorts by recently updated; an
        // index on UpdatedAt keeps the picker query cheap as the catalog grows.
        builder.HasIndex(d => d.UpdatedAt);
    }
}
