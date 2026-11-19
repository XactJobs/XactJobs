using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Services;

public interface IXactJobsUIService
{
    // Dashboard Statistics
    Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JobCountsOverTimeDto>> GetJobCountsOverTimeAsync(
        DateTime from, DateTime to, TimeSpan interval, CancellationToken ct = default);

    /// <summary>
    /// The queues of the workers configured in this process, and of the pending jobs and the periodic jobs, the default queue first.
    /// The pending jobs' queues are found with one index seek per queue (see <c>JobGetQueuesAsync</c>).
    /// </summary>
    Task<IReadOnlyList<string>> GetQueuesAsync(CancellationToken ct = default);

    /// <summary>
    /// The pending and running jobs of one queue, the earliest scheduled first, read in the order of the primary key. The count
    /// stops at <c>MaxCount</c>.
    /// </summary>
    Task<PagedResultDto<JobDto>> GetScheduledJobsAsync(
        string queue, int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// A pending or running job, by its primary key (see <see cref="JobKey"/>).
    /// </summary>
    Task<JobDto?> GetJobAsync(JobKey key, CancellationToken ct = default);

    /// <summary>
    /// Moves a pending job to the history as cancelled. Not allowed for running jobs, nor for runs of periodic jobs:
    /// those are stopped by pausing the periodic job.
    /// </summary>
    Task<ActionResultDto> CancelJobAsync(JobKey key, string? cancelledBy, CancellationToken ct = default);

    /// <summary>
    /// Makes a pending job due now. Not allowed for running jobs, nor for continuations that wait for their batch.
    /// </summary>
    Task<ActionResultDto> RunJobNowAsync(JobKey key, CancellationToken ct = default);

    /// <summary>
    /// The history of a category processed from <paramref name="from"/>, until <paramref name="to"/> (excluded) if it's set, the
    /// latest first, a page at a time after the row of the <paramref name="cursor"/> (null, or text that isn't a cursor: the latest
    /// page). The count stops at <c>MaxCount</c>.
    /// </summary>
    Task<CursorPageDto<JobHistoryDto>> GetHistoryAsync(
        HistoryCategory category, DateTime from, DateTime? to, string? queue, string? cursor, int pageSize, CancellationToken ct = default);
    Task<JobHistoryDto?> GetJobHistoryByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// Enqueues a failed or cancelled job, or a continuation that was skipped, again as a new job, outside of any batch.
    /// A run of a periodic job is enqueued as a one-off job, so that it doesn't add a second chain of runs to the periodic job.
    /// </summary>
    Task<ActionResultDto> RetryJobAsync(long historyId, CancellationToken ct = default);

    // Batches

    /// <summary>
    /// The batches with jobs left, the earliest scheduled first. The count stops at <c>MaxCount</c>.
    /// </summary>
    Task<PagedResultDto<BatchDto>> GetActiveBatchesAsync(int page, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// The batches without jobs left whose last job was processed from <paramref name="from"/>, until <paramref name="to"/> (excluded)
    /// if it's set, the latest finished first, a page at a time after the batch of the <paramref name="cursor"/>, as long as their
    /// history is kept. The count stops at <c>MaxCount</c>.
    /// </summary>
    Task<CursorPageDto<BatchDto>> GetFinishedBatchesAsync(
        DateTime from, DateTime? to, string? cursor, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// The batch with its jobs, its history and its continuations, or null if nothing is left of it.
    /// </summary>
    Task<BatchDetailsDto?> GetBatchAsync(Guid id, CancellationToken ct = default);

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
