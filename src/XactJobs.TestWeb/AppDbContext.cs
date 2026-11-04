using Microsoft.EntityFrameworkCore;
using XactJobs.EntityConfigurations;

namespace XactJobs.TestWeb;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyXactJobsConfigurations(Database.ProviderName, false);
    }
}
