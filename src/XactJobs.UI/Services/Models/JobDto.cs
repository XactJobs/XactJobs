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

    public bool IsProcessing => LeasedUntil.HasValue && LeasedUntil > DateTime.UtcNow;
    public bool IsPeriodic => PeriodicJobId != null;
    public bool IsDue => ScheduledAt <= DateTime.UtcNow;
    public string DisplayTypeName => DisplayNames.Type(TypeName);
    public string DisplayQueue => DisplayNames.Queue(Queue);
}
