using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Services;

public interface IXactJobsUIService
{
    // Dashboard Statistics
    Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<JobCountsOverTimeDto>> GetJobCountsOverTimeAsync(
        DateTime from, DateTime to, TimeSpan interval, CancellationToken ct = default);

    // Pending/Scheduled Jobs
    Task<PagedResultDto<JobDto>> GetScheduledJobsAsync(
        int page, int pageSize, string? queue = null, CancellationToken ct = default);
    Task<JobDto?> GetJobByIdAsync(long id, CancellationToken ct = default);
    Task<bool> DeleteJobAsync(long id, CancellationToken ct = default);
    Task<bool> RequeueJobAsync(long id, CancellationToken ct = default);

    // Job History
    Task<PagedResultDto<JobHistoryDto>> GetSucceededJobsAsync(
        int page, int pageSize, string? queue = null, CancellationToken ct = default);
    Task<PagedResultDto<JobHistoryDto>> GetFailedJobsAsync(
        int page, int pageSize, string? queue = null, CancellationToken ct = default);
    Task<JobHistoryDto?> GetJobHistoryByIdAsync(long id, CancellationToken ct = default);
    Task<bool> RetryFailedJobAsync(long id, CancellationToken ct = default);

    // Periodic Jobs
    Task<PagedResultDto<PeriodicJobDto>> GetPeriodicJobsAsync(
        int page, int pageSize, CancellationToken ct = default);
    Task<PeriodicJobDto?> GetPeriodicJobByIdAsync(string id, CancellationToken ct = default);
    Task<bool> TogglePeriodicJobAsync(string id, bool isActive, CancellationToken ct = default);
    Task<bool> TriggerPeriodicJobNowAsync(string id, CancellationToken ct = default);
    Task<bool> DeletePeriodicJobAsync(string id, CancellationToken ct = default);
}
