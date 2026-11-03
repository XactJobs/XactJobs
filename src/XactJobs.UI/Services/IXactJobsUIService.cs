using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Services;

public interface IXactJobsUIService
{
    // Dashboard Statistics
    Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JobCountsOverTimeDto>> GetJobCountsOverTimeAsync(
        DateTime from, DateTime to, TimeSpan interval, CancellationToken ct = default);

    /// <summary>
    /// The queues of the workers configured in this process, and of the pending jobs and the periodic jobs.
    /// </summary>
    Task<IReadOnlyList<string>> GetQueuesAsync(CancellationToken ct = default);

    // Pending/Scheduled Jobs
    Task<PagedResultDto<JobDto>> GetScheduledJobsAsync(
        int page, int pageSize, string? queue = null, CancellationToken ct = default);
    Task<JobDto?> GetJobByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Moves a pending job to the history as cancelled. Not allowed for running jobs, nor for runs of periodic jobs:
    /// those are stopped by pausing the periodic job.
    /// </summary>
    Task<ActionResultDto> CancelJobAsync(long id, string? cancelledBy, CancellationToken ct = default);

    /// <summary>
    /// Makes a pending job due now. Not allowed for running jobs.
    /// </summary>
    Task<ActionResultDto> RunJobNowAsync(long id, CancellationToken ct = default);

    // Job History
    Task<PagedResultDto<JobHistoryDto>> GetHistoryAsync(
        HistoryCategory category, int page, int pageSize, string? queue = null, CancellationToken ct = default);
    Task<JobHistoryDto?> GetJobHistoryByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Enqueues a failed or cancelled job again, as a new job. A run of a periodic job is enqueued as a one-off job,
    /// so that it doesn't add a second chain of runs to the periodic job.
    /// </summary>
    Task<ActionResultDto> RetryJobAsync(long historyId, CancellationToken ct = default);

    // Periodic Jobs
    Task<PagedResultDto<PeriodicJobDto>> GetPeriodicJobsAsync(
        int page, int pageSize, CancellationToken ct = default);
    Task<PeriodicJobDto?> GetPeriodicJobByIdAsync(string id, CancellationToken ct = default);
    Task<ActionResultDto> SetPeriodicJobActiveAsync(string id, bool isActive, CancellationToken ct = default);

    /// <summary>
    /// Runs a periodic job now, by making its pending run due now. Not allowed if it's paused or already running.
    /// </summary>
    Task<ActionResultDto> TriggerPeriodicJobAsync(string id, CancellationToken ct = default);
    Task<ActionResultDto> DeletePeriodicJobAsync(string id, CancellationToken ct = default);
}
