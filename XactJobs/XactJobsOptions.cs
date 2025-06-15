using Microsoft.EntityFrameworkCore;

namespace XactJobs
{
    public class XactJobsOptionsBase<TDbContext> where TDbContext: DbContext
    {
        /// <summary>
        /// How many jobs to fetch and executee at a time. Default is 100.
        /// </summary>
        public int BatchSize { get; set; } = 100;

        /// <summary>
        /// Number of paraller workers running. Default 4.
        /// </summary>
        public int WorkerCount { get; set; } = 4;

        /// <summary>
        /// Max degree of paralell jobs running per worker. Default -1 (means ProcessorCount)
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = -1;

        /// <summary>
        /// Default 120 (jobs runner will prolong the lease every 1/2 of this time)
        /// </summary>
        public int LeaseDurationInSeconds { get; set; } = 120;

        /// <summary>
        /// Database polling interval in seconds, per worker (Default 10 seconds)
        /// </summary>
        public int PollingIntervalInSeconds { get; set; } = 10;

        /// <summary>
        /// Time the workers will wait for to clear pending leases, on worker stop. (Default 10 seconds)
        /// </summary>
        public int ClearLeaseTimeoutInSeconds { get; set; } = 10;

        /// <summary>
        /// Time the workers will wait for before retrying when a DB error occurs. (Default 10 seconds).
        /// These are internal worker errors (usually when the worker cannot reach the database) - not job errors.
        /// </summary>
        public int WorkerErrorRetryDelayInSeconds { get; set; } = 10;
    }

    public class XactJobsOptions<TDbContext>: XactJobsOptionsBase<TDbContext> where TDbContext: DbContext
    {
        /// <summary>
        /// List of isolated queues for which workers will be started
        /// </summary>
        public Dictionary<string, XactJobsOptionsBase<TDbContext>> IsolatedQueues { get; set; } = [];
    }
}
