namespace XactJobs.UI.Services.Models;

public record JobHistoryDto
{
    public long Id { get; init; }
    public DateTime ProcessedAt { get; init; }
    public DateTime ScheduledAt { get; init; }
    public XactJobStatus Status { get; init; }
    public string TypeName { get; init; } = "";
    public string MethodName { get; init; } = "";
    public string Queue { get; init; } = "";
    public int ErrorCount { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorStackTrace { get; init; }
    public string? PeriodicJobId { get; init; }
    public Guid? BatchId { get; init; }
    public Guid? AwaitsBatchId { get; init; }
    public XactJobContinuation? ContinuationCondition { get; init; }

    /// <summary>
    /// The time the job was originally scheduled for, if it differs from <see cref="ScheduledAt"/>: a retry, or a run moved to now.
    /// The job gets it as XactJobContext.OriginallyScheduledAt.
    /// </summary>
    public DateTime? OriginallyScheduledAt { get; init; }

    /// <summary>Failed and cancelled jobs can be run again, and so can continuations that were skipped.</summary>
    public bool CanRetry => Status == XactJobStatus.Cancelled || IsSkippedContinuation;

    public bool IsSkippedContinuation => Status == XactJobStatus.Skipped && AwaitsBatchId != null;
    public string? DisplayCondition => DisplayNames.Condition(ContinuationCondition);
    public string DisplayTypeName => DisplayNames.Type(TypeName);

    public string DisplayMethodName => DisplayNames.Method(MethodName);
    public string DisplayQueue => DisplayNames.Queue(Queue);
}
