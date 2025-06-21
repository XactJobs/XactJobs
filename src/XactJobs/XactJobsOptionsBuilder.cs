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

        public TBuilder WithBatchSize(int batchSize)
        {
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
        public XactJobsOptionsBuilder<TDbContext> WithHistoryRetentionPeriodInDays(int days)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(days, 1);

            Options.HistoryRetentionPeriodInDays = days;
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
