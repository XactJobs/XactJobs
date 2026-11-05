using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XactJobs.UI.Api;

namespace XactJobs.UI.DependencyInjection;

public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Maps XactJobs UI dashboard and API endpoints.
    /// <para>
    /// It maps the Razor Pages of the application too (mapping them again is harmless).
    /// With <see cref="XactJobsUIOptionsBuilder.RequireAuthorization"/>, the application has to use the authentication and
    /// authorization middleware.
    /// </para>
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapXactJobsUI(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<XactJobsUIOptions>();

        endpoints.MapXactJobsApi(options);

        endpoints.ServiceProvider.GetRequiredService<XactJobsAssets>().Map(endpoints);

        // the access to the pages is set by the conventions added in AddXactJobsUI
        endpoints.MapRazorPages();

        if (options.Access == XactJobsUIAccess.LocalRequestsOnly)
        {
            endpoints.ServiceProvider.GetService<ILoggerFactory>()?
                .CreateLogger("XactJobs.UI")
                .LogWarning("The XactJobs dashboard at {BasePath} accepts only local requests. Configure its access with RequireAuthorization, " +
                            "RequireAuthorizationPolicy or AllowAnonymous. Behind a reverse proxy on the same machine, every request looks local, " +
                            "unless the forwarded headers middleware is configured.", options.BasePath);
        }

        return endpoints;
    }
}
