using Microsoft.EntityFrameworkCore;
using OpenTelemetryDashboard.Core.Domain;
using OpenTelemetryDashboard.Dashboards.Domain;
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

    public DbSet<Dashboard> Dashboards => Set<Dashboard>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TelemetryDbContext).Assembly);
        modelBuilder.ApplySnakeCaseNaming();
    }
}
