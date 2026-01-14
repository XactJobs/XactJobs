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
    public string DisplayTypeName => TypeName.Split(',')[0].Split('.').LastOrDefault() ?? TypeName;
}
