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

    public TimeSpan Duration => ProcessedAt - ScheduledAt;
    public string DisplayTypeName => TypeName.Split(',')[0].Split('.').LastOrDefault() ?? TypeName;
}
