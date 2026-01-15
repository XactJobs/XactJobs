using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using XactJobs.UI.Api;

namespace XactJobs.UI.DependencyInjection;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Maps XactJobs UI dashboard and API endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapXactJobsUI(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<XactJobsUIOptions>();

        // Map API endpoints
        var apiGroup = endpoints.MapGroup(options.ApiBasePath);
        if (options.RequireAuthorization && !string.IsNullOrEmpty(options.AuthorizationPolicy))
        {
            apiGroup.RequireAuthorization(options.AuthorizationPolicy);
        }
        else if (options.RequireAuthorization)
        {
            apiGroup.RequireAuthorization();
        }

        XactJobsApiEndpoints.MapXactJobsApi(endpoints, options.ApiBasePath);

        // Map Razor Pages with the configured base path
        var pagesGroup = endpoints.MapGroup(options.BasePath);
        if (options.RequireAuthorization && !string.IsNullOrEmpty(options.AuthorizationPolicy))
        {
            pagesGroup.RequireAuthorization(options.AuthorizationPolicy);
        }
        else if (options.RequireAuthorization)
        {
            pagesGroup.RequireAuthorization();
        }

        // Map Razor Pages routes
        endpoints.MapRazorPages();

        return endpoints;
    }
}
