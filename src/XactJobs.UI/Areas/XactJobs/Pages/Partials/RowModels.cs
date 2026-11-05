using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Pages;

/// <summary>
/// The badges of a job of a batch, or of a continuation. A badge for <paramref name="CurrentBatchId"/> isn't shown, it's the page's own batch.
/// </summary>
public record BatchBadgesModel(XactJobsPageModel Owner, Guid? BatchId, Guid? AwaitsBatchId, string? Condition, Guid? CurrentBatchId = null);

/// <summary>
/// A row of a table of pending jobs.
/// </summary>
public record JobRowModel(XactJobsPageModel Owner, JobDto Job, Guid? CurrentBatchId = null);

/// <summary>
/// The Retry button of a history row, if the job can be retried.
/// </summary>
public record RetryButtonModel(XactJobsPageModel Owner, JobHistoryDto Job);

/// <summary>
/// A table of history rows of all the categories, with their status.
/// </summary>
public record HistoryRowsModel(XactJobsPageModel Owner, IReadOnlyList<JobHistoryDto> Jobs, Guid? CurrentBatchId = null);
