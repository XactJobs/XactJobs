using Microsoft.EntityFrameworkCore;
using XactJobs.TestModel.PostgreSql;

namespace XactJobs.TestWorker
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = Host.CreateApplicationBuilder(args);

            builder.Services.AddDbContext<UserDbContext>(x =>
            {
                x.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnectionString"));
            });

            builder.Services.AddXactJobs<UserDbContext>(x =>
            {
                x.WithPollingInterval(2);
            });

            builder.Services.AddTransient<TestJob>();

            var host = builder.Build();
            host.Run();
        }
    }
}