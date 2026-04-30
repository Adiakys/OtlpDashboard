using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Dashboards.Domain;
using OpenTelemetryDashboard.Persistence.Metrics.Entities;
using OpenTelemetryDashboard.Persistence.Naming;

namespace OpenTelemetryDashboard.Persistence;

public sealed class TelemetryDbContext : DbContext
{
    public TelemetryDbContext(DbContextOptions<TelemetryDbContext> options)
        : base(options)
    {
    }

    public DbSet<Resource> Resources => Set<Resource>();

    public DbSet<Span> Spans => Set<Span>();

    public DbSet<LogRecord> Logs => Set<LogRecord>();

    public DbSet<InstrumentRecord> Instruments => Set<InstrumentRecord>();

    public DbSet<MetricPointRecord> MetricPoints => Set<MetricPointRecord>();

    public DbSet<Dashboard> Dashboards => Set<Dashboard>();

    public DbSet<WidgetDefinition> WidgetDefinitions => Set<WidgetDefinition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TelemetryDbContext).Assembly);
        modelBuilder.ApplySnakeCaseNaming();
    }
}
