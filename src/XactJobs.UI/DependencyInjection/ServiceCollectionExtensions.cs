using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using XactJobs.UI.Api;
using XactJobs.UI.Security;
using XactJobs.UI.Services;

namespace XactJobs.UI.DependencyInjection;

public static class ServiceCollectionExtensions
{
    internal const string AreaName = "XactJobs";

    /// <summary>
    /// Adds XactJobs UI services to the service collection.
    /// By default, the dashboard only accepts requests from the local machine: see <see cref="XactJobsUIOptionsBuilder"/>.
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

        var options = builder.Options;

        services.AddSingleton(options);
        services.AddScoped<IXactJobsUIService, XactJobsUIService<TDbContext>>();
        services.AddSingleton<XactJobsAssets>();

        // Add Razor Pages support for the UI with custom route prefix convention
        services.AddRazorPages(pages =>
        {
            // Apply route prefix convention to all XactJobs UI pages
            pages.Conventions.Add(new XactJobsPageRouteModelConvention(options.BasePath));

            switch (options.Access)
            {
                case XactJobsUIAccess.Authorized:
                    if (options.AuthorizationPolicy == null)
                    {
                        pages.Conventions.AuthorizeAreaFolder(AreaName, "/");
                    }
                    else
                    {
                        pages.Conventions.AuthorizeAreaFolder(AreaName, "/", options.AuthorizationPolicy);
                    }
                    break;

                case XactJobsUIAccess.LocalRequestsOnly:
                    pages.Conventions.AddAreaFolderApplicationModelConvention(AreaName, "/",
                        model => model.Filters.Add(new LocalRequestsOnlyFilter()));
                    break;

                case XactJobsUIAccess.Anonymous:
                    pages.Conventions.AllowAnonymousToAreaFolder(AreaName, "/");
                    break;
            }
        });

        return services;
    }
}
