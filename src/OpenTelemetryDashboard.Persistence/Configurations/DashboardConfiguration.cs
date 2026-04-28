using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Dashboards.Validation;

namespace OpenTelemetryDashboard.Persistence.Configurations;

public sealed class DashboardConfiguration : IEntityTypeConfiguration<Dashboard>
{
    public void Configure(EntityTypeBuilder<Dashboard> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Dashboards");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.Name)
            .IsRequired()
            .HasMaxLength(DashboardValidation.MaxNameLength);

        // Layout is an opaque JSON document owned by the SPA. Stored as
        // unbounded text and capped at the validation layer (256 KB) so
        // every provider can accept it without column-size juggling.
        builder.Property(d => d.LayoutJson)
            .IsRequired();

        builder.Property(d => d.UpdatedAt);

        // App-managed optimistic concurrency token. Incremented in
        // EfCoreDashboardStore.SaveDefaultAsync before SaveChanges; portable
        // across SQLite/PostgreSQL/SQL Server without provider-specific
        // rowversion or xmin plumbing.
        builder.Property(d => d.RowVersion)
            .IsConcurrencyToken();
    }
}
