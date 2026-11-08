// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using XactJobs.Annotations;

namespace XactJobs
{
    public class XactJobsOptionsBuilderBase<TDbContext, TOptions, TBuilder>
        where TDbContext : DbContext
        where TOptions: XactJobsOptionsBase<TDbContext>, new()
        where TBuilder: XactJobsOptionsBuilderBase<TDbContext, TOptions, TBuilder>
    {
        public TOptions Options { get; private set; } = new();

        public TBuilder WithMaxDegreeOfParallelism(int maxDegreeOfParallelism)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism, 1);

            Options.MaxDegreeOfParallelism = maxDegreeOfParallelism;
            return (this as TBuilder)!;
        }

        /// <summary>
        /// The most jobs a worker claims and holds at a time, running <see cref="XactJobsOptionsBase{TDbContext}.MaxDegreeOfParallelism"/>
        /// of them at once. Default: twice MaxDegreeOfParallelism, so each slot has a job waiting for it. At or below MaxDegreeOfParallelism,
        /// a worker only claims jobs it can start immediately. A larger batch means fewer claims, but the jobs a worker holds wait for its
        /// free slots, also when another worker is idle.
        /// </summary>
        public TBuilder WithBatchSize(int batchSize)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);

            Options.BatchSize = batchSize;
            return (this as TBuilder)!;
        }

        public TBuilder WithWorkerCount(int workerCount)
        {
            Options.WorkerCount = workerCount;
            return (this as TBuilder)!;
        }

        public TBuilder WithPollingInterval(int intervalInSeconds)
        {
            Options.PollingIntervalInSeconds = intervalInSeconds;
            return (this as TBuilder)!;
        }

        public TBuilder WithLeaseDuration(int durationInSeconds)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(durationInSeconds, 1);

            Options.LeaseDurationInSeconds = durationInSeconds;
            return (this as TBuilder)!;
        }

        public TBuilder WithShutdownTimeout(int timeoutInSeconds)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(timeoutInSeconds);

            Options.ShutdownTimeoutInSeconds = timeoutInSeconds;
            return (this as TBuilder)!;
        }

        public TBuilder WithRetryStrategy(IRetryStrategy retryStrategy)
        {
            Options.RetryStrategy = retryStrategy;
            return (this as TBuilder)!;
        }

        public TBuilder WithPeriodicJob([InstantHandle] Expression<Action> jobExpression, string id, string cronExpression)
        {
            Options.PeriodicJobs[id] = (jobExpression, cronExpression);
            return (this as TBuilder)!;
        }

        public TBuilder WithPeriodicJob<T>( [InstantHandle] Expression<Action<T>> jobExpression, string id, string cronExpression)
        {
            Options.PeriodicJobs[id] = (jobExpression, cronExpression);
            return (this as TBuilder)!;
        }

        public TBuilder WithPeriodicJob([InstantHandle] Expression<Func<Task>> jobExpression, string id, string cronExpression)
        {
            Options.PeriodicJobs[id] = (jobExpression, cronExpression);
            return (this as TBuilder)!;
        }

        public TBuilder WithPeriodicJob<T>([InstantHandle] Expression<Func<T, Task>> jobExpression, string id, string cronExpression)
        {
            Options.PeriodicJobs[id] = (jobExpression, cronExpression);
            return (this as TBuilder)!;
        }
    }

    public class XactJobsQueueOptionsBuilder<TDbContext>: XactJobsOptionsBuilderBase<TDbContext, XactJobsOptionsBase<TDbContext>, XactJobsQueueOptionsBuilder<TDbContext>>
        where TDbContext : DbContext
    {
    }
    
    public class XactJobsOptionsBuilder<TDbContext>: XactJobsOptionsBuilderBase<TDbContext, XactJobsOptions<TDbContext>, XactJobsOptionsBuilder<TDbContext>>
        where TDbContext : DbContext
    {
        /// <summary>
        /// Deletes the history older than this many days, every hour, with a periodic job (xj_history_cleanup). By default the history is
        /// kept. Set it the same on every node: a node without it removes the cleanup job.
        /// </summary>
        public XactJobsOptionsBuilder<TDbContext> WithHistoryRetentionPeriodInDays(int days)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

            Options.HistoryRetentionPeriodInDays = days;
            return this;
        }

        public XactJobsOptionsBuilder<TDbContext> WithPeriodicJobReconciliationInterval(int intervalInSeconds)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(intervalInSeconds, 1);

            Options.PeriodicJobReconciliationIntervalInSeconds = intervalInSeconds;
            return this;
        }

        /// <summary>
        /// What the worker does when a job calls <c>SaveChanges</c> on its scoped <typeparamref name="TDbContext"/> outside a transaction:
        /// warn (the default), throw, or ignore it. Applies to all the queues.
        /// </summary>
        public XactJobsOptionsBuilder<TDbContext> WithSaveOutsideTransaction(XactJobSaveCheck check)
        {
            if (!Enum.IsDefined(check)) throw new ArgumentOutOfRangeException(nameof(check), check, null);

            Options.SaveOutsideTransaction = check;
            return this;
        }

        /// <summary>
        /// Also tags the jobs' metrics (attempts, duration, start delay) with the job's type and method, as <c>xactjobs.job.type</c>
        /// and <c>xactjobs.job.method</c>. Off by default: it makes one series per job method instead of per queue. Applies to all
        /// the queues.
        /// </summary>
        public XactJobsOptionsBuilder<TDbContext> WithJobTagsInMetrics(bool enabled = true)
        {
            Options.JobTagsInMetrics = enabled;
            return this;
        }

        public XactJobsOptionsBuilder<TDbContext> WithPriorityQueue(Action<XactJobsQueueOptionsBuilder<TDbContext>>? configureAction = null)
        {
            WithIsolatedQueue(QueueNames.Priority, options =>
            {
                options
                    .WithPollingInterval(4)
                    .WithWorkerCount(2);

                configureAction?.Invoke(options);
            });
            return this;
        }

        public XactJobsOptionsBuilder<TDbContext> WithLongRunningQueue(Action<XactJobsQueueOptionsBuilder<TDbContext>>? configureAction = null)
        {
            WithIsolatedQueue(QueueNames.LongRunning, options =>
            {
                options
                    .WithPollingInterval(10)
                    .WithWorkerCount(2);

                configureAction?.Invoke(options);
            });
            return this;
        }

        public XactJobsOptionsBuilder<TDbContext> WithIsolatedQueue(string queueName, Action<XactJobsQueueOptionsBuilder<TDbContext>>? configureAction = null)
        {
            var builder = new XactJobsQueueOptionsBuilder<TDbContext>();

            configureAction?.Invoke(builder);

            Options.IsolatedQueues[queueName] = builder.Options;

            return this;
        }
    }
}
