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

        protected override async Task EnsurePeriodicJobsAsync(TDbContext db, CancellationToken stoppingToken)
        {
            await EnsurePeriodicJobsForQueueAsync(db, null, _options, stoppingToken)
                                .ConfigureAwait(false);

            foreach (var (queueName, queueOptions) in _options.IsolatedQueues)
            {
                await EnsurePeriodicJobsForQueueAsync(db, queueName, queueOptions, stoppingToken)
                    .ConfigureAwait(false);
            }

            await EnsureMaintenanceJobsAsync(db, stoppingToken)
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

        private static async Task EnsureMaintenanceJobsAsync(TDbContext db, CancellationToken stoppingToken)
        {
            await db.JobEnsurePeriodicAsync<XactJobMaintenance<TDbContext>>(
                x => x.CleanupJobHistoryAsync(CancellationToken.None), 
                "xj_history_cleanup", 
                Cron.HourInterval(1), 
                stoppingToken).ConfigureAwait(false);
        }
    }
}
