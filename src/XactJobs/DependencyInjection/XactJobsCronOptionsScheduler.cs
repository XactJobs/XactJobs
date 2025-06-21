using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using XactJobs.Cron;
using XactJobs.Internal;

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

        protected override async Task EnsurePeriodicJobsAsync(TDbContext db, CancellationToken stoppingToken)
        {
            await EnsurePeriodicJobsForQueue(db, null, _options, stoppingToken)
                                .ConfigureAwait(false);

            foreach (var (queueName, queueOptions) in _options.IsolatedQueues)
            {
                await EnsurePeriodicJobsForQueue(db, queueName, queueOptions, stoppingToken)
                    .ConfigureAwait(false);
            }

            await EnsureMaintenanceJobsAsync(db, stoppingToken)
                .ConfigureAwait(false);

            await db.SaveChangesAsync(stoppingToken)
                .ConfigureAwait(false);
        }

        private static async Task EnsurePeriodicJobsForQueue(TDbContext db,
                                                             string? queueName,
                                                             XactJobsOptionsBase<TDbContext> options,
                                                             CancellationToken stoppingToken)
        {
            foreach (var (name, (lambdaExp, cronExp)) in options.PeriodicJobs)
            {
                await db.JobAddOrUpdatePeriodicAsync(lambdaExp, name, cronExp, queueName, stoppingToken)
                    .ConfigureAwait(false);
            }
        }

        private static async Task EnsureMaintenanceJobsAsync(TDbContext db, CancellationToken stoppingToken)
        {
            await db.JobEnsurePeriodicAsync<XactJobMaintenance<TDbContext>>(
                x => x.CleanupJobHistoryAsync(CancellationToken.None), 
                "xj_history_cleanup", 
                CronBuilder.HourInterval(1), 
                stoppingToken);
        }
    }
}
