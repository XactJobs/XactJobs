using Microsoft.EntityFrameworkCore;

namespace XactJobs.TestModel
{
    public class UserDbContext: DbContext
    {
        public DbSet<User> User { get; set; }

        public UserDbContext()
        {
        }

        public UserDbContext(DbContextOptions<UserDbContext> options): base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyXactJobsConfigurations(Database.ProviderName, false);
        }
    }
}
