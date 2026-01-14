namespace XactJobs.UI.Services.Models;

public record PeriodicJobDto
{
    public string Id { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public string CronExpression { get; init; } = "";
    public string TypeName { get; init; } = "";
    public string MethodName { get; init; } = "";
    public string Queue { get; init; } = "";
    public bool IsActive { get; init; }
    public int Version { get; init; }
    public DateTime? NextRunAt { get; init; }

    public string DisplayTypeName => TypeName.Split(',')[0].Split('.').LastOrDefault() ?? TypeName;
}
