using Microsoft.EntityFrameworkCore;

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
                //options.UseSqlite(builder.Configuration.GetConnectionString("SqliteConnectionString"));
            });

            builder.Services.AddXactJobs<UserDbContext>(options =>
            {
                options.WithPriorityQueue();
                options.WithLongRunningQueue();

                options.WithPeriodicJob<TestJob>(
                    x => x.RunTestJobAsync(10, "test_1", new TestJob.TestPayload { PayloadId = 9, PayloadData = "Nine" }, CancellationToken.None),
                    "test_1",
                    Cron.SecondInterval(10));
            });

            var host = builder.Build();

            /*
            // re-create the DB (this is for TESTING ONLY)
            using (var scope = host.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<UserDbContext>();

                //db.Database.EnsureDeleted();
                db.Database.EnsureCreated();
            }
            */

            host.Run();
        }
    }
}