using Microsoft.EntityFrameworkCore;
using XactJobs.Cron;

namespace XactJobs.TestWorker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Services.AddDbContext<UserDbContext>(options =>
            {
                options.UseNpgsql(builder.Configuration.GetConnectionString("PostgresConnectionString"));
                //options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServerConnectionString"));
                //options.UseMySql(builder.Configuration.GetConnectionString("MySqlConnectionString"), ServerVersion.Parse("8.5.4"));
                //options.UseOracle(builder.Configuration.GetConnectionString("OracleConnectionString"));
            });

            builder.Services.AddXactJobs<UserDbContext>(options =>
            {
                options.WithPeriodicJob<TestJob>
                (
                    "test_1", 
                    CronBuilder.EverySeconds(10), 
                    x => x.RunAsync(10, "test_1", Guid.NewGuid(), CancellationToken.None)
                );
            });

            builder.Services.AddTransient<TestJob>();

            var host = builder.Build();

            // re-create the DB (this is for TESTING ONLY)
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();

                db.Database.EnsureDeleted();
                db.Database.EnsureCreated();
            }

            host.Run();
        }
    }
}