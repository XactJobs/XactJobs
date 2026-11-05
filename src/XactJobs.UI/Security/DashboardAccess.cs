using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace XactJobs.UI.Security;

internal static class DashboardAccess
{
    /// <summary>
    /// The access of the API and of the dashboard's files, the same as the pages' (set by conventions in AddXactJobsUI).
    /// </summary>
    public static RouteGroupBuilder RequireDashboardAccess(this RouteGroupBuilder group, XactJobsUIOptions options)
    {
        switch (options.Access)
        {
            case XactJobsUIAccess.Authorized:
                if (options.AuthorizationPolicy == null)
                {
                    group.RequireAuthorization();
                }
                else
                {
                    group.RequireAuthorization(options.AuthorizationPolicy);
                }
                break;

            case XactJobsUIAccess.LocalRequestsOnly:
                group.AddEndpointFilter(async (context, next) =>
                    LocalRequests.IsLocal(context.HttpContext)
                        ? await next(context)
                        : Results.StatusCode(StatusCodes.Status403Forbidden));
                break;

            case XactJobsUIAccess.Anonymous:
                group.AllowAnonymous();
                break;
        }

        return group;
    }
}
