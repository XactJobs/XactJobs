using Microsoft.EntityFrameworkCore;

namespace XactJobs
{
    public class XactJobsOptions<TDbContext> where TDbContext: DbContext
    {
        /// <summary>
        /// How many jobs to fetch and executee at a time. Default is 100.
        /// </summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>
        /// Default -1 (means ProcessorCount)
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = -1;

        /// <summary>
        /// Default 120 (jobs runner will prolong the lease every 1/2 of this time)
        /// </summary>
        public int LeaseDurationInSeconds { get; set; } = 120;

        /// <summary>
        /// Database polling interval in seconds (Default 2 seconds)
        /// </summary>
        public int PollingIntervalInSeconds { get; set; } = 2;

        /// <summary>
        /// Time the workers will wait for to clear pending leases, on worker stop. (Default 10 seconds)
        /// </summary>
        public int ClearLeaseTimeoutInSeconds { get; set; } = 10;

        /// <summary>
        /// Time the workers will wait for before retrying when a DB error occurs. (Default 10 seconds).
        /// These are internal worker errors (usually when the worker cannot reach the database) - not job errors.
        /// </summary>
        public int WorkerErrorRetryDelayInSeconds { get; set; } = 10;

        /// <summary>
        /// List of isolated queues for which workers will be started
        /// </summary>
        public List<string> IsolatedQueues { get; set; } = [];
    }
}
