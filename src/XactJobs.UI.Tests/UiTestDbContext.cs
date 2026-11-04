using Microsoft.EntityFrameworkCore;

namespace XactJobs.UI.Tests;

public class UiTestDbContext : DbContext
{
    public UiTestDbContext(DbContextOptions<UiTestDbContext> options) : base(options)
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

public class SampleJobs
{
    public void Run()
    {
    }

    public void Send(string to, int count)
    {
    }

    public void Send(int id, int count)
    {
    }
}
