using Microsoft.EntityFrameworkCore;
using XactJobs.UI;

namespace XactJobs.TestWeb
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddDbContext<UserDbContext>(options =>
            {
                options.UseNpgsql(builder.Configuration.GetConnectionString("PostgresConnectionString"));
                //options.UseSqlServer(builder.Configuration.GetConnectionString("SqlServerConnectionString"));
                //options.UseMySql(builder.Configuration.GetConnectionString("MySqlConnectionString"), ServerVersion.Parse("8.5.4"));
                //options.UseOracle(builder.Configuration.GetConnectionString("OracleConnectionString"));
                //options.UseSqlite(builder.Configuration.GetConnectionString("SqliteConnectionString"));
            });

            //builder.Services.AddXactJobs<UserDbContext>();

            builder.Services.AddControllers();
            builder.Services.AddXactJobsUI<UserDbContext>();

            var app = builder.Build();

            // Configure the HTTP request pipeline.

            app.UseHttpsRedirection();

            app.UseAuthorization();

            app.UseXactJobsUI();

            app.MapControllers();

            app.Run();
        }
    }
}
