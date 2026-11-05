namespace XactJobs.UI.Services.Models;

/// <summary>
/// A batch: the jobs with the same batch id. There's no batch table: a batch is active while some of its jobs are left,
/// and finished afterwards, as long as its history is kept.
/// </summary>
public record BatchDto
{
    public Guid Id { get; init; }

    /// <summary>The jobs left: scheduled, running, or continuations that wait for another batch.</summary>
    public int PendingJobs { get; init; }
    public int RunningJobs { get; init; }
    public int SucceededJobs { get; init; }

    /// <summary>Jobs that ran out of retries, were cancelled, or were continuations that were skipped.</summary>
    public int FailedJobs { get; init; }

    /// <summary>The continuations that wait for this batch.</summary>
    public int AwaitingContinuations { get; init; }

    /// <summary>The earliest scheduled time of the jobs left, without continuations that wait for another batch.</summary>
    public DateTime? NextRunAt { get; init; }
    public DateTime? LastProcessedAt { get; init; }

    /// <summary>The type and method of one of the batch's jobs, to tell batches apart.</summary>
    public string? TypeName { get; init; }
    public string? MethodName { get; init; }

    public int TotalJobs => PendingJobs + SucceededJobs + FailedJobs;
    public bool IsFinished => PendingJobs == 0;
    public string DisplayId => DisplayNames.Batch(Id);
    public string? DisplayTypeName => TypeName == null ? null : DisplayNames.Type(TypeName);
    public string? DisplayMethodName => MethodName == null ? null : DisplayNames.Method(MethodName);
}

/// <summary>
/// A batch with its jobs and continuations, each list up to <c>MaxPageSize</c> rows.
/// </summary>
public record BatchDetailsDto
{
    public BatchDto Batch { get; init; } = new();

    /// <summary>The jobs left in the batch.</summary>
    public IReadOnlyList<JobDto> Jobs { get; init; } = [];

    /// <summary>The finished jobs of the batch, and their retried attempts, latest first.</summary>
    public IReadOnlyList<JobHistoryDto> History { get; init; } = [];

    /// <summary>The continuations that wait for the batch, and their retries.</summary>
    public IReadOnlyList<JobDto> PendingContinuations { get; init; } = [];

    /// <summary>The continuations that ran or were skipped, and their attempts, latest first.</summary>
    public IReadOnlyList<JobHistoryDto> FinishedContinuations { get; init; } = [];

    /// <summary>The batches that the jobs of this batch continue, when they are continuations themselves.</summary>
    public IReadOnlyList<Guid> ContinuesBatches { get; init; } = [];
}

public enum BatchState
{
    /// <summary>Batches with jobs left.</summary>
    Active,
    /// <summary>Batches without jobs left, whose history is still kept.</summary>
    Finished,
}
