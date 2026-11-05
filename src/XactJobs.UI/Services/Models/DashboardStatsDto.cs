namespace XactJobs.UI.Services.Models;

public record DashboardStatsDto
{
    /// <summary>Jobs whose scheduled time has passed, waiting for a worker.</summary>
    public int DueJobs { get; init; }
    /// <summary>Jobs scheduled for later, without the continuations that wait for their batch.</summary>
    public int ScheduledJobs { get; init; }
    /// <summary>Continuations that wait for their batch to finish.</summary>
    public int AwaitingContinuations { get; init; }
    /// <summary>Jobs leased by a worker at the moment.</summary>
    public int ProcessingJobs { get; init; }
    public int SucceededLast24h { get; init; }
    /// <summary>Jobs that ran out of retries.</summary>
    public int FailedLast24h { get; init; }
    /// <summary>Failed attempts that were scheduled to run again.</summary>
    public int RetriedLast24h { get; init; }
    public int PeriodicJobsActive { get; init; }
    public int PeriodicJobsInactive { get; init; }
    /// <summary>Succeeded out of succeeded and failed jobs. Retried attempts don't count, their job isn't finished.</summary>
    public double SuccessRateLast24h { get; init; }
}
