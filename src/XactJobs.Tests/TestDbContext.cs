using Microsoft.EntityFrameworkCore;

namespace XactJobs.Tests;

public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    public DbSet<XactJob> Jobs => Set<XactJob>();
    public DbSet<XactJobHistory> JobHistory => Set<XactJobHistory>();
    public DbSet<XactJobPeriodic> PeriodicJobs => Set<XactJobPeriodic>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyXactJobsConfigurations(Database.ProviderName, excludeFromMigrations: false);
    }
}
