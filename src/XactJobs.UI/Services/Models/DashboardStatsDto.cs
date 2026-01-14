namespace XactJobs.UI.Services.Models;

public record DashboardStatsDto
{
    public int PendingJobs { get; init; }
    public int ProcessingJobs { get; init; }
    public int SucceededLast24h { get; init; }
    public int FailedLast24h { get; init; }
    public int PeriodicJobsActive { get; init; }
    public int PeriodicJobsInactive { get; init; }
    public double SuccessRateLast24h { get; init; }
}
