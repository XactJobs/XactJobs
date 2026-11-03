namespace XactJobs.UI.Services.Models;

/// <summary>
/// How the dashboard groups the history rows.
/// </summary>
public enum HistoryCategory
{
    /// <summary>Completed.</summary>
    Succeeded,
    /// <summary>Ran out of retries: recorded as cancelled, with the error of the last attempt.</summary>
    Failed,
    /// <summary>Cancelled from the dashboard: recorded as cancelled, without an error stack trace.</summary>
    Cancelled,
    /// <summary>A failed attempt, after which the job was scheduled to run again.</summary>
    Retried,
    /// <summary>A run of a periodic job that was paused, deleted or changed.</summary>
    Skipped,
}
