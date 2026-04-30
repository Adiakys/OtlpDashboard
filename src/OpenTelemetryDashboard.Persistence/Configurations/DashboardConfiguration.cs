using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenTelemetryDashboard.Dashboards.Domain;

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
            .HasMaxLength(32);

        builder.Property(d => d.UpdatedAt);

        builder.HasMany(d => d.Widgets)
            .WithOne()
            .HasPrincipalKey(d => d.Id)
            .HasForeignKey(d => d.DashboardId)
            .OnDelete(DeleteBehavior.Cascade);
        
        // App-managed optimistic concurrency token. Incremented in
        // EfCoreDashboardStore.UpdateAsync before SaveChanges; portable
        // across SQLite/PostgreSQL/SQL Server without provider-specific
        // rowversion or xmin plumbing.
        builder.Property(d => d.RowVersion)
            .IsConcurrencyToken();

        // The default dashboard is no longer seeded via HasData. The
        // historic `SeedDefaultDashboard` migration still runs against
        // pre-existing databases (idempotent), and `BuiltinDashboardSeeder`
        // handles fresh installs and file-driven defaults at runtime.
    }
}

public sealed class DashboardWidgetsConfiguration : IEntityTypeConfiguration<DashboardWidget>
{
    public void Configure(EntityTypeBuilder<DashboardWidget> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("DashboardWidgets");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.DashboardId).IsRequired();

        builder.Property(w => w.Kind)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(w => w.X);
        builder.Property(w => w.Y);
        builder.Property(w => w.W);
        builder.Property(w => w.H);

        // Per-widget config is an opaque JSON document owned by the SPA.
        // Stored as unbounded text; the validation layer enforces a size
        // cap so every provider can accept it without column-size juggling.
        builder.Property(w => w.ConfigJson)
            .IsRequired();

        builder.HasIndex(w => w.DashboardId);
    }
}