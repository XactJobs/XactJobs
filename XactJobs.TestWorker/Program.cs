using Microsoft.EntityFrameworkCore;
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
                    .WithPollingInterval(2)
                    .WithIsolatedQueue("long_running", queueOptions =>
                    {
                        queueOptions
                            .WithLeaseDuration(10)
                            .WithPollingInterval(10);
                        
                    })
                    .WithIsolatedQueue("test");
            });

            builder.Services.AddTransient<TestJob>();

            var host = builder.Build();
            host.Run();
        }
    }
}