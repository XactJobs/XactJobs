using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
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

        return endpoints;
    }

    /// <summary>
    /// Adds XactJobs UI middleware for serving embedded static files.
    /// Call this before UseRouting().
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseXactJobsUI(this IApplicationBuilder app)
    {
        // Static files are served automatically by Razor Class Library
        // via the StaticWebAssetBasePath configured in .csproj
        return app;
    }
}
