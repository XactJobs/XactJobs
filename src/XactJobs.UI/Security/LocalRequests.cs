using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace XactJobs.UI.Security;

internal static class LocalRequests
{
    /// <summary>
    /// A request without a remote address (e.g. from an in-memory test server) doesn't count as local.
    /// </summary>
    public static bool IsLocal(HttpContext httpContext)
    {
        var remote = httpContext.Connection.RemoteIpAddress;

        return remote != null && System.Net.IPAddress.IsLoopback(remote);
    }
}

/// <summary>
/// Rejects the requests to the dashboard pages that don't come from the local machine.
/// </summary>
internal sealed class LocalRequestsOnlyFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!LocalRequests.IsLocal(context.HttpContext))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}
