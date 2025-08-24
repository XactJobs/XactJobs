using Microsoft.EntityFrameworkCore;
using XactJobs.UI;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ServiceCollectionUIExtensions
    {
        public static IServiceCollection AddXactJobsUI<TDbContext>(this IServiceCollection services)
            where TDbContext : DbContext
        {
            return services.AddScoped(x => new XactJobsDbContextAccessor(x.GetRequiredService<TDbContext>()));
        }
    }
}