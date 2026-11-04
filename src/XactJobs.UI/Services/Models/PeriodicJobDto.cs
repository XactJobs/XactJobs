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
    /// <summary>The scheduled time of the pending run of the current version, if any.</summary>
    public DateTime? NextRunAt { get; init; }
    /// <summary>A run of the current version is leased by a worker.</summary>
    public bool IsRunning { get; init; }

    public string DisplayTypeName => DisplayNames.Type(TypeName);

    public string DisplayMethodName => DisplayNames.Method(MethodName);
    public string DisplayQueue => DisplayNames.Queue(Queue);
}
