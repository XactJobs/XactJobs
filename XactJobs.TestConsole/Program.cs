using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using XactJobs.TestModel;

namespace XactJobs.TestConsole
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var db = new DbContextFactory().CreateDbContext(args);

            //db.JobEnqueue(() => User.MyJob(1, "Sina", Guid.NewGuid()));
            db.JobEnqueue(() => User.MyJobAsync(1, "Long", Guid.NewGuid(), CancellationToken.None)); //, "long_running");
            //db.JobEnqueue(() => User.MyJob(1, "Test", Guid.NewGuid()), "test");

            db.SaveChanges();

            Console.ReadLine();
        }
    }

    public class DbContextFactory : IDesignTimeDbContextFactory<UserDbContext>
    {
        public UserDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();

            var dbContextOptions = new DbContextOptionsBuilder<UserDbContext>()
                .UseOracle(configuration.GetConnectionString("DefaultConnectionString"))
                .Options;

            return new UserDbContext(dbContextOptions);
        }
    }
}
