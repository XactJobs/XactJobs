using Microsoft.EntityFrameworkCore;
using System.Globalization;

using XactJobs.EntityConfigurations;

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
                optionsBuilder.UseNpgsql();
            }

            optionsBuilder.UseSnakeCaseNamingConvention(CultureInfo.InvariantCulture);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.ApplyConfiguration(new XactJobEntityConfiguration(Database.ProviderName));
        }

    }
}
