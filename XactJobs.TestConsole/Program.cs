using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using XactJobs;
using XactJobs.TestModel.PostgreSql;

namespace XactJobs.TestConsole
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var db = new DbContextFactory().CreateDbContext(args);

        }

        public static void MyJob(int id, string name, Guid guid)
        {
            Console.WriteLine(id);
            Console.WriteLine(name);
            Console.WriteLine(guid);
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
                .UseNpgsql(configuration.GetConnectionString("DefaultConnectionString"))
                .Options;

            return new UserDbContext(dbContextOptions);
        }
    }
}
