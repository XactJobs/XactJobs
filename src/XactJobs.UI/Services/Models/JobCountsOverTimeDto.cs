namespace XactJobs.UI.Services.Models;

public record JobCountsOverTimeDto
{
    public DateTime Timestamp { get; init; }
    public int Succeeded { get; init; }
    public int Failed { get; init; }
    public int Cancelled { get; init; }
    public int Skipped { get; init; }
}
