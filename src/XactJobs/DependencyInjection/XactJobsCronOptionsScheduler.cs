// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        protected override TimeSpan ReconciliationInterval => TimeSpan.FromSeconds(_options.PeriodicJobReconciliationIntervalInSeconds);

        protected override async Task EnsurePeriodicJobsAsync(TDbContext db, CancellationToken stoppingToken)
        {
            await EnsurePeriodicJobsForQueueAsync(db, null, _options, stoppingToken)
                                .ConfigureAwait(false);

            foreach (var (queueName, queueOptions) in _options.IsolatedQueues)
            {
                await EnsurePeriodicJobsForQueueAsync(db, queueName, queueOptions, stoppingToken)
                    .ConfigureAwait(false);
            }

            await EnsureMaintenanceJobsAsync(db, _options, stoppingToken)
                .ConfigureAwait(false);

            await db.SaveChangesAsync(stoppingToken)
                .ConfigureAwait(false);
        }

        private static async Task EnsurePeriodicJobsForQueueAsync(TDbContext db,
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

        internal const string HistoryCleanupJobId = "xj_history_cleanup";

        /// <summary>
        /// The history cleanup only runs if the app sets a retention period. Without one, a cleanup job added by an older version, which
        /// had a default of 30 days, is removed, so that it stops deleting the history.
        /// </summary>
        private static async Task EnsureMaintenanceJobsAsync(TDbContext db, XactJobsOptions<TDbContext> options, CancellationToken stoppingToken)
        {
            if (options.HistoryRetentionPeriodInDays == null)
            {
                await db.JobDeletePeriodicAsync(HistoryCleanupJobId, stoppingToken)
                    .ConfigureAwait(false);

                return;
            }

            await db.JobEnsurePeriodicAsync<XactJobMaintenance<TDbContext>>(
                x => x.CleanupJobHistoryAsync(CancellationToken.None), 
                HistoryCleanupJobId, 
                Cron.HourInterval(1), 
                stoppingToken).ConfigureAwait(false);
        }
    }
}
