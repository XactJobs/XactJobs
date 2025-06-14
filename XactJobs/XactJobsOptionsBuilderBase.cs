using Microsoft.EntityFrameworkCore;

namespace XactJobs
{
    public class XactJobsOptionsBuilderBase<TDbContext>
        where TDbContext : DbContext
    {
        public XactJobsOptionsBase<TDbContext> Options { get; private set; } = new();

        public XactJobsOptionsBuilderBase<TDbContext> WithBatchSize(int batchSize)
        {
            Options.BatchSize = batchSize;
            return this;
        }

        public XactJobsOptionsBuilderBase<TDbContext> WithPollingInterval(int intervalInSeconds)
        {
            Options.PollingIntervalInSeconds = intervalInSeconds;
            return this;
        }
    }

    public class XactJobsOptionsBuilder<TDbContext>
        where TDbContext : DbContext
    {
        public XactJobsOptions<TDbContext> Options { get; private set; } = new();

        public XactJobsOptionsBuilder<TDbContext> WithBatchSize(int batchSize)
        {
            Options.BatchSize = batchSize;
            return this;
        }

        public XactJobsOptionsBuilder<TDbContext> WithPollingInterval(int intervalInSeconds)
        {
            Options.PollingIntervalInSeconds = intervalInSeconds;
            return this;
        }

        public XactJobsOptionsBuilder<TDbContext> WithIsolatedQueue(string queueName, Action<XactJobsOptionsBuilderBase<TDbContext>>? configureAction = null)
        {
            var builder = new XactJobsOptionsBuilderBase<TDbContext>();

            configureAction?.Invoke(builder);

            Options.IsolatedQueues[queueName] = builder.Options;

            return this;
        }
    }
}
