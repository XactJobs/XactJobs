using Microsoft.EntityFrameworkCore;
using XactJobs.Cron;
using XactJobs.TestModel;
using XactJobs.TestModel.PostgreSql;

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
                    .UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnectionString"));
            });

            builder.Services.AddXactJobs<UserDbContext>(options =>
            {
                options
                    .WithPeriodicJob("test_1", CronBuilder.EverySeconds(10), () => User.MyJobAsync(10, "test cron 1", Guid.NewGuid(), CancellationToken.None));

                options.WithIsolatedQueue("priority", queueOptions =>
                {
                    queueOptions
                        .WithPeriodicJob("test_2", CronBuilder.EveryWeekDayAt(new TimeSpan(8, 0, 0), DayOfWeek.Saturday),
                            () => User.MyJobAsync(10, "test cron 2", Guid.NewGuid(), CancellationToken.None));
                });
            });

            builder.Services.AddTransient<TestJob>();

            var host = builder.Build();
            host.Run();
        }
    }
}