using Microsoft.EntityFrameworkCore;

namespace SampleServer;

public sealed class Counter
{
    public int Id { get; set; }
    public int Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CounterDbContext(DbContextOptions<CounterDbContext> options) : DbContext(options)
{
    public DbSet<Counter> Counters => Set<Counter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var counter = modelBuilder.Entity<Counter>();
        counter.ToTable("sample_counters");
        counter.HasKey(c => c.Id);
        counter.Property(c => c.Id).ValueGeneratedNever();
    }
}
