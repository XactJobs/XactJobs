using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XactJobs.DependencyInjection
{
    internal class XactJobsCronOptionsScheduler<TDbContext> : XactJobsCronScheduler<TDbContext> where TDbContext : DbContext
    {
        private readonly XactJobsOptions<TDbContext> _options;

        public XactJobsCronOptionsScheduler(XactJobsOptions<TDbContext> options, IServiceScopeFactory scopeFactory, ILogger<XactJobsCronScheduler<TDbContext>> logger)
            : base(scopeFactory, logger)
        {
            _options = options;
        }

        protected override async Task EnsurePeriodicJobs(TDbContext db, CancellationToken stoppingToken)
        {
            await EnsurePeriodicJobsForQueue(db, null, _options, stoppingToken)
                                .ConfigureAwait(false);

            foreach (var (queueName, queueOptions) in _options.IsolatedQueues)
            {
                await EnsurePeriodicJobsForQueue(db, queueName, queueOptions, stoppingToken)
                    .ConfigureAwait(false);
            }

            await db.SaveChangesAsync(stoppingToken)
                .ConfigureAwait(false);
        }

        private async Task EnsurePeriodicJobsForQueue(TDbContext db,
                                                      string? queueName,
                                                      XactJobsOptionsBase<TDbContext> options,
                                                      CancellationToken stoppingToken)
        {
            foreach (var (name, (lambdaExp, cronExp, isActive)) in options.PeriodicJobs)
            {
                await db.JobAddOrUpdatePeriodicAsync(lambdaExp, name, cronExp, queueName, isActive, stoppingToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
