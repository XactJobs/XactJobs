// This file is part of XactJobs.
//
// XactJobs is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// XactJobs is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XactJobs.Internal;

namespace XactJobs.DependencyInjection
{
    public abstract class XactJobsCronScheduler<TDbContext> : BackgroundService where TDbContext : DbContext
    {
        protected ILogger<XactJobsCronScheduler<TDbContext>> Logger { get; }
        protected IServiceScopeFactory ScopeFactory { get; }

        /// <summary>
        /// How often to look for active periodic jobs without a pending run. Default 5 minutes.
        /// </summary>
        protected virtual TimeSpan ReconciliationInterval => TimeSpan.FromMinutes(5);

        protected abstract Task EnsurePeriodicJobsAsync(TDbContext db, CancellationToken stoppingToken);

        public XactJobsCronScheduler(IServiceScopeFactory scopeFactory, ILogger<XactJobsCronScheduler<TDbContext>> logger)
        {
            ScopeFactory = scopeFactory;
            Logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var periodicJobsEnsured = false;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunCycleAsync(ensurePeriodicJobs: !periodicJobsEnsured, stoppingToken)
                        .ConfigureAwait(false);

                    periodicJobsEnsured = true;

                    await Task.Delay(ReconciliationInterval, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (Exception) when (stoppingToken.IsCancellationRequested)
                {
                    // stopping: a cancelled command can also throw the provider's own exception (SqlClient's SqlException)
                    return;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to create periodic jobs. Retrying in {RetryIn}", ReconciliationInterval);

                    await WaitAfterFailureAsync(stoppingToken)
                        .ConfigureAwait(false);
                }
            }
        }

        private async Task WaitAfterFailureAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(ReconciliationInterval, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // the loop ends on its own
            }
        }

        private async Task RunCycleAsync(bool ensurePeriodicJobs, CancellationToken stoppingToken)
        {
            using var scope = ScopeFactory.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

            var dialect = db.GetSqlDialect();

            // a retrying execution strategy, if the app enabled one, only allows explicit transactions inside of it
            await db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                db.ChangeTracker.Clear(); // in case this is a retry

                using var tx = await db.Database.BeginTransactionAsync(token)
                    .ConfigureAwait(false);

                await dialect.AcquireTableLockAsync(db, dialect.XactJobSchema, dialect.XactJobPeriodicTable, token)
                    .ConfigureAwait(false);

                try
                {
                    if (ensurePeriodicJobs)
                    {
                        await EnsurePeriodicJobsAsync(db, token)
                            .ConfigureAwait(false);

                        await db.SaveChangesAsync(token)
                            .ConfigureAwait(false);
                    }

                    await ReconcilePeriodicJobsAsync(db, token)
                        .ConfigureAwait(false);

                    await tx.CommitAsync(token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    // also when the cycle fails or is cancelled (e.g. on shutdown): MySQL's lock belongs to the session, not to the
                    // transaction, so its connection would go back to the pool still holding it, and the other nodes' cycles would wait for it
                    await ReleaseTableLockAsync(db, dialect)
                        .ConfigureAwait(false);
                }
            }, stoppingToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Never throws, so that it doesn't hide the cycle's own error. The other databases' locks end with the transaction.
        /// </summary>
        private async Task ReleaseTableLockAsync(TDbContext db, ISqlDialect dialect)
        {
            try
            {
                await dialect.ReleaseTableLockAsync(db, dialect.XactJobSchema, dialect.XactJobPeriodicTable, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // if the connection is broken, its session ends, and the lock with it
                Logger.LogWarning(ex, "Failed to release the lock of the periodic jobs");
            }
        }

        /// <summary>
        /// Each run of a periodic job schedules the next one, so a periodic job without a pending run would never run again.
        /// This happens if the row was added directly to the periodic table, or if the pending row got lost.
        /// There can also be a pending row of an older version, which will be skipped, so only the current version counts.
        /// </summary>
        private async Task ReconcilePeriodicJobsAsync(TDbContext db, CancellationToken cancellationToken)
        {
            var periodicJobs = await db.Set<XactJobPeriodic>()
                .AsNoTracking()
                .Where(p => !db.Set<XactJob>().Any(j => j.PeriodicJobId == p.Id && (j.PeriodicJobVersion ?? 1) == p.Version))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // filtered here because the Oracle provider can't translate IsActive, which has a value converter
            foreach (var periodicJob in periodicJobs.Where(p => p.IsActive))
            {
                try
                {
                    var job = db.ScheduleNextRun(periodicJob);

                    Logger.LogWarning("Periodic job {Id} had no pending run, scheduled the next one at {ScheduledAt}", periodicJob.Id, job.ScheduledAt);
                }
                catch (Exception ex)
                {
                    // e.g. an invalid cron expression in a manually added row: it shouldn't stop the other periodic jobs
                    Logger.LogError(ex, "Failed to schedule the next run of periodic job {Id}", periodicJob.Id);
                }
            }

            await db.SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}

