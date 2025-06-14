using Microsoft.EntityFrameworkCore;

namespace XactJobs
{
    public class XactJobsOptionsBuilder<TDbContext> where TDbContext: DbContext
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
    }
}
