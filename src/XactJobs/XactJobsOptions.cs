// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace XactJobs
{
    public class XactJobsOptionsBase<TDbContext> where TDbContext: DbContext
    {
        /// <summary>
        /// Number of workers polling this queue. Default 2.
        /// The workers are started with an initial delay, to distribute their polling in time.
        /// By default, 2 workers are started with a 6 second polling interval, so the initial
        /// delay between them is (6 seconds / 2 workers) = 3 seconds.
        /// </summary>
        public int WorkerCount { get; set; } = 2;

        /// <summary>
        /// The most jobs running at the same time per worker. Default -1 (means ProcessorCount).
        /// A worker also holds jobs waiting for a slot: see <see cref="BatchSize"/>, by default twice this.
        /// </summary>
        public int MaxDegreeOfParallelism { get; set; } = -1;

        /// <summary>
        /// The most jobs a worker claims and holds at a time, running <see cref="MaxDegreeOfParallelism"/> of them at once.
        /// Default -1 (means twice MaxDegreeOfParallelism: each slot has a job waiting for it). At or below MaxDegreeOfParallelism,
        /// a worker only claims jobs it can start immediately.
        /// A larger batch means fewer claims, but the jobs a worker holds wait for its free slots, also when another worker is idle.
        /// </summary>
        public int BatchSize { get; set; } = -1;

        /// <summary>
        /// Default 120 (jobs runner will prolong the lease every 3/4 of this time)
        /// </summary>
        public int LeaseDurationInSeconds { get; set; } = 120;

        /// <summary>
        /// Database polling interval in seconds, per worker (Default 6 seconds).
        /// The workers will be started with initial delay, to distribute workers execution in time.
        /// By default, 2 workers will be started with 6 second polling interval. 
        /// The initial delay between workers is (6 seconds / 2 workers) = 3 seconds.
        /// It means these workers will take turns every 3 seconds.
        /// </summary>
        public int PollingIntervalInSeconds { get; set; } = 6;

        /// <summary>
        /// On worker stop, how long running jobs get to finish before they are cancelled. (Default 10 seconds)
        /// Make sure this fits in the shutdown timeout of the host, along with <see cref="ClearLeaseTimeoutInSeconds"/>
        /// and 5 seconds for the jobs to react to the cancellation.
        /// </summary>
        public int ShutdownTimeoutInSeconds { get; set; } = 10;

        /// <summary>
        /// Time the workers will wait for to clear pending leases, on worker stop. (Default 10 seconds)
        /// </summary>
        public int ClearLeaseTimeoutInSeconds { get; set; } = 10;

        /// <summary>
        /// Retry strategy - if not set, the default retry strategy is used. <see cref="DefaultRetryStrategy"/>
        /// </summary>
        public IRetryStrategy RetryStrategy { get; set; } = new DefaultRetryStrategy();

        /// <summary>
        /// The periodic jobs ensured at start, by id: the job expression, and its schedule (a cron expression, with seconds, and its
        /// time zone).
        /// </summary>
        public Dictionary<string, (LambdaExpression JobExpression, XactCronSchedule Schedule)> PeriodicJobs { get; set; } = [];
    }

    public class XactJobsOptions<TDbContext>: XactJobsOptionsBase<TDbContext> where TDbContext: DbContext
    {
        /// <summary>
        /// How many days the history is kept. Default null: the history is kept, and nothing is deleted.
        /// When it's set, a periodic job (xj_history_cleanup) deletes the older history every hour, except the history of the batches
        /// that continuations still wait for. Set it the same on every node: a node without it removes the cleanup job.
        /// </summary>
        public int? HistoryRetentionPeriodInDays { get; set; }

        /// <summary>
        /// How often each node looks for active periodic jobs without a pending run, and schedules their next run.
        /// Default 300 seconds.
        /// </summary>
        public int PeriodicJobReconciliationIntervalInSeconds { get; set; } = 300;

        /// <summary>
        /// The assemblies whose methods with <see cref="XactPeriodicJobAttribute"/> are periodic jobs. Read when the services are added.
        /// </summary>
        public List<System.Reflection.Assembly> PeriodicJobAssemblies { get; set; } = [];

        /// <summary>
        /// What the worker does when a job calls <c>SaveChanges</c> on its scoped <typeparamref name="TDbContext"/> outside a transaction,
        /// which loses the exactly-once database effects. Default <see cref="XactJobSaveCheck.Warn"/>. Applies to all the queues.
        /// </summary>
        public XactJobSaveCheck SaveOutsideTransaction { get; set; } = XactJobSaveCheck.Warn;

        /// <summary>
        /// Also tags the jobs' metrics (attempts, duration, start delay) with the job's type and method. Off by default: one series
        /// per job method instead of per queue. Applies to all the queues.
        /// </summary>
        public bool JobTagsInMetrics { get; set; }

        /// <summary>
        /// The <see cref="IXactJobFilter"/> types run around every job, the first one outermost. Registered as scoped services when the
        /// XactJobs services are added. Applies to all the queues.
        /// </summary>
        public List<Type> JobFilters { get; set; } = [];

        /// <summary>
        /// The <see cref="IXactJobFailureHandler"/> types told when a job has failed for good, in this order. Registered as scoped
        /// services when the XactJobs services are added. Applies to all the queues.
        /// </summary>
        public List<Type> JobFailureHandlers { get; set; } = [];

        /// <summary>
        /// The <see cref="IXactJobsWorkerGate"/> type asked before every claim, whether this node's workers may claim jobs. Registered as
        /// a singleton when the XactJobs services are added, unless the app registered it. Default null: the workers always claim.
        /// Applies to all the queues.
        /// </summary>
        public Type? WorkerGate { get; set; }

        /// <summary>
        /// List of isolated queues for which workers will be started
        /// </summary>
        public Dictionary<string, XactJobsOptionsBase<TDbContext>> IsolatedQueues { get; set; } = [];
    }
}
