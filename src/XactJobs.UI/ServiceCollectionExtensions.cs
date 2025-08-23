using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace XactJobs.UI
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddXactJobsUI<TDbContext>(this IServiceCollection services)
            where TDbContext : DbContext
        {
            // Register the concrete controller for the user's DbContext
            return services.AddTransient<Controllers.JobsController<TDbContext>>();
        }
    }
}