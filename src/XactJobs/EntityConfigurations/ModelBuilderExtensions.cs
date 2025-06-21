using XactJobs.EntityConfigurations;

namespace Microsoft.EntityFrameworkCore
{
    public static class ModelBuilderExtensions
    {
        public static void ApplyXactJobsConfigurations(this ModelBuilder modelBuilder, string? providerName, bool excludeFromMigrations = true)
        {
            modelBuilder.ApplyConfiguration(new XactJobEntityConfiguration(providerName, excludeFromMigrations));
            modelBuilder.ApplyConfiguration(new XactJobHistoryEntityConfiguration(providerName, excludeFromMigrations));
            modelBuilder.ApplyConfiguration(new XactJobPeriodicEntityConfiguration(providerName, excludeFromMigrations));
        }
    }
}
