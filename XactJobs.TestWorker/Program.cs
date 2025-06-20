using Microsoft.EntityFrameworkCore;
using XactJobs.Cron;

using XactJobs.TestModel.MySql;

namespace XactJobs.TestWorker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Services.AddDbContext<UserDbContext>(options =>
            {
                options
                    .UseMySql(builder.Configuration.GetConnectionString("MySqlConnectionString"), ServerVersion.Parse("8.4.5"));
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
            host.Run();
        }
    }
}