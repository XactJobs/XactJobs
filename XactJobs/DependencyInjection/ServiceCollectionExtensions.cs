using Microsoft.EntityFrameworkCore;
using XactJobs;
using XactJobs.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddXactJobs<TDbContext>(this IServiceCollection services, Action<XactJobsOptionsBuilder<TDbContext>>? configureOptions = null)
        where TDbContext: DbContext
    {
        var optionsBuilder = new XactJobsOptionsBuilder<TDbContext>();

        configureOptions?.Invoke(optionsBuilder);

        services.AddSingleton(optionsBuilder.Options);
        services.AddHostedService<XactJobsRunnerDispatcher<TDbContext>>();
        services.AddHostedService<XactJobsCronOptionsScheduler<TDbContext>>();

        return services;
    }
}
