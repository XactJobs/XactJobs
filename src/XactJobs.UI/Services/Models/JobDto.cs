namespace XactJobs.UI.Services.Models;

public record JobDto
{
    public long Id { get; init; }
    public DateTime ScheduledAt { get; init; }
    public string TypeName { get; init; } = "";
    public string MethodName { get; init; } = "";
    public string Queue { get; init; } = "";
    public int ErrorCount { get; init; }
    public DateTime? LeasedUntil { get; init; }
    public Guid? Leaser { get; init; }
    public string? PeriodicJobId { get; init; }
    public Guid? BatchId { get; init; }
    public Guid? AwaitsBatchId { get; init; }
    public XactJobContinuation? ContinuationCondition { get; init; }

    /// <summary>For a continuation that waits for its batch: the jobs left in the batch.</summary>
    public int? BatchJobsLeft { get; init; }

    /// <summary>A continuation that waits for its batch to finish. Its scheduled time is a placeholder.</summary>
    public bool IsAwaitingBatch => AwaitsBatchId != null && ScheduledAt == XactJobBase.AwaitingBatchScheduledAt;

    public bool IsProcessing => LeasedUntil.HasValue && LeasedUntil > DateTime.UtcNow;
    public bool IsPeriodic => PeriodicJobId != null;
    public bool IsDue => ScheduledAt <= DateTime.UtcNow;
    public string? DisplayCondition => DisplayNames.Condition(ContinuationCondition);
    public string DisplayTypeName => DisplayNames.Type(TypeName);

    public string DisplayMethodName => DisplayNames.Method(MethodName);
    public string DisplayQueue => DisplayNames.Queue(Queue);
}
