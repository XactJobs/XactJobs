namespace XactJobs.UI;

public enum XactJobsUIAccess
{
    /// <summary>Only requests from the local machine (loopback address). The default.</summary>
    LocalRequestsOnly,
    /// <summary>Requests that satisfy an authorization policy, see <see cref="XactJobsUIOptions.AuthorizationPolicy"/>.</summary>
    Authorized,
    /// <summary>Any request. Only for development, or when the access is restricted in another way.</summary>
    Anonymous,
}

public class XactJobsUIOptions
{
    /// <summary>
    /// Base path for the UI dashboard. Default is "/XactJobs".
    /// </summary>
    public string BasePath { get; set; } = "/XactJobs";

    /// <summary>
    /// Base path for the API endpoints. Default is "/api/XactJobs".
    /// </summary>
    public string ApiBasePath { get; set; } = "/api/XactJobs";

    /// <summary>
    /// Page size for job listings. Default is 20.
    /// </summary>
    public int DefaultPageSize { get; set; } = 20;

    /// <summary>
    /// Who can use the dashboard and the API. Default is <see cref="XactJobsUIAccess.LocalRequestsOnly"/>.
    /// </summary>
    public XactJobsUIAccess Access { get; set; } = XactJobsUIAccess.LocalRequestsOnly;

    /// <summary>
    /// Authorization policy name when <see cref="Access"/> is <see cref="XactJobsUIAccess.Authorized"/>.
    /// Null means the default policy (an authenticated user).
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// Whether the API endpoints that change data require an antiforgery token. Default is true.
    /// The dashboard sends the token. Turn it off only if the API is called by clients that don't use cookies.
    /// </summary>
    public bool RequireAntiforgery { get; set; } = true;

    /// <summary>
    /// Dashboard title displayed in the UI.
    /// </summary>
    public string DashboardTitle { get; set; } = "XactJobs Dashboard";

    /// <summary>
    /// Poll interval for auto-refresh in seconds. Default is 5.
    /// </summary>
    public int RefreshIntervalSeconds { get; set; } = 5;
}
