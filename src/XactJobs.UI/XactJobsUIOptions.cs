namespace XactJobs.UI;

public class XactJobsUIOptions
{
    /// <summary>
    /// Base path for the UI dashboard. Default is "/xactjobs".
    /// </summary>
    public string BasePath { get; set; } = "/xactjobs";

    /// <summary>
    /// Base path for the API endpoints. Default is "/api/xactjobs".
    /// </summary>
    public string ApiBasePath { get; set; } = "/api/xactjobs";

    /// <summary>
    /// Page size for job listings. Default is 20.
    /// </summary>
    public int DefaultPageSize { get; set; } = 20;

    /// <summary>
    /// Enable authorization requirement. When true, the consumer must configure
    /// authorization policies.
    /// </summary>
    public bool RequireAuthorization { get; set; }

    /// <summary>
    /// Authorization policy name when RequireAuthorization is true.
    /// </summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>
    /// Dashboard title displayed in the UI.
    /// </summary>
    public string DashboardTitle { get; set; } = "XactJobs Dashboard";

    /// <summary>
    /// Poll interval for auto-refresh in seconds. Default is 30.
    /// </summary>
    public int RefreshIntervalSeconds { get; set; } = 30;
}
