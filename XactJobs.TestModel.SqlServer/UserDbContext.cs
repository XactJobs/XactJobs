using Microsoft.EntityFrameworkCore;

namespace XactJobs.TestModel.PostgreSql
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

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);

            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlServer();
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyXactJobsConfigurations(Database.ProviderName, false);
        }

    }
}
