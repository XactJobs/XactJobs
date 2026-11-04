namespace XactJobs.UI;

public class XactJobsUIOptionsBuilder
{
    public XactJobsUIOptions Options { get; } = new();

    public XactJobsUIOptionsBuilder WithBasePath(string basePath)
    {
        Options.BasePath = basePath.TrimEnd('/');
        return this;
    }

    public XactJobsUIOptionsBuilder WithApiBasePath(string apiBasePath)
    {
        Options.ApiBasePath = apiBasePath.TrimEnd('/');
        return this;
    }

    public XactJobsUIOptionsBuilder WithPageSize(int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        Options.DefaultPageSize = pageSize;
        return this;
    }

    /// <summary>
    /// Requires an authenticated user (the default authorization policy). The application has to configure
    /// authentication and authorization, and use their middleware.
    /// </summary>
    public XactJobsUIOptionsBuilder RequireAuthorization()
    {
        Options.Access = XactJobsUIAccess.Authorized;
        Options.AuthorizationPolicy = null;
        return this;
    }

    /// <summary>
    /// Requires the given authorization policy. The application has to configure authentication and authorization,
    /// and use their middleware.
    /// </summary>
    public XactJobsUIOptionsBuilder RequireAuthorizationPolicy(string policyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyName);
        Options.Access = XactJobsUIAccess.Authorized;
        Options.AuthorizationPolicy = policyName;
        return this;
    }

    /// <summary>
    /// Allows only requests from the local machine. This is the default.
    /// Behind a reverse proxy on the same machine, every request looks local, unless the forwarded headers middleware is configured.
    /// </summary>
    public XactJobsUIOptionsBuilder AllowLocalRequestsOnly()
    {
        Options.Access = XactJobsUIAccess.LocalRequestsOnly;
        Options.AuthorizationPolicy = null;
        return this;
    }

    /// <summary>
    /// Allows anyone to see and change the jobs. Only for development, or when the access is restricted in another way.
    /// </summary>
    public XactJobsUIOptionsBuilder AllowAnonymous()
    {
        Options.Access = XactJobsUIAccess.Anonymous;
        Options.AuthorizationPolicy = null;
        return this;
    }

    /// <summary>
    /// The API endpoints that change data don't require an antiforgery token. Use it only if the API is called by clients
    /// that don't authenticate with cookies.
    /// </summary>
    public XactJobsUIOptionsBuilder DisableAntiforgery()
    {
        Options.RequireAntiforgery = false;
        return this;
    }

    public XactJobsUIOptionsBuilder WithDashboardTitle(string title)
    {
        Options.DashboardTitle = title;
        return this;
    }

    public XactJobsUIOptionsBuilder WithRefreshInterval(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(seconds, 5);
        Options.RefreshIntervalSeconds = seconds;
        return this;
    }
}
