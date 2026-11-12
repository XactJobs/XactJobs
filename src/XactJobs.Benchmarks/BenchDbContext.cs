using Microsoft.EntityFrameworkCore;

namespace XactJobs.Benchmarks;

public sealed class BenchDbContext(DbContextOptions<BenchDbContext> options) : DbContext(options)
{
    public DbSet<XactJob> Jobs => Set<XactJob>();
    public DbSet<XactJobHistory> JobHistory => Set<XactJobHistory>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyXactJobsConfigurations(Database.ProviderName, includeInMigrations: true);
    }
}
