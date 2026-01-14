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

    public XactJobsUIOptionsBuilder RequireAuthorizationPolicy(string policyName)
    {
        Options.RequireAuthorization = true;
        Options.AuthorizationPolicy = policyName;
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
