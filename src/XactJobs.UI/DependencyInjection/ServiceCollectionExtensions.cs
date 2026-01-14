using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XactJobs.UI.Services;

namespace XactJobs.UI.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds XactJobs UI services to the service collection.
    /// </summary>
    /// <typeparam name="TDbContext">The DbContext type that has XactJobs entities configured.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration for UI options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddXactJobsUI<TDbContext>(
        this IServiceCollection services,
        Action<XactJobsUIOptionsBuilder>? configureOptions = null)
        where TDbContext : DbContext
    {
        var builder = new XactJobsUIOptionsBuilder();
        configureOptions?.Invoke(builder);

        services.AddSingleton(builder.Options);
        services.AddScoped<IXactJobsUIService, XactJobsUIService<TDbContext>>();

        return services;
    }
}
