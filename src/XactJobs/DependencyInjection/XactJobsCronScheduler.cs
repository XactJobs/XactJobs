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
using Microsoft.EntityFrameworkCore.Storage;
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

        protected abstract Task EnsurePeriodicJobsAsync(TDbContext db, CancellationToken stoppingToken);

        public XactJobsCronScheduler(IServiceScopeFactory scopeFactory, ILogger<XactJobsCronScheduler<TDbContext>> logger)
        {
            ScopeFactory = scopeFactory;
            Logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var scope = ScopeFactory.CreateScope();

            IDbContextTransaction? tx = null; 
            try
            {
                var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

                var dialect = db.Database.ProviderName.ToSqlDialect();

                tx = db.Database.BeginTransaction();

                await dialect.AcquireTableLockAsync(db, dialect.XactJobSchema, dialect.XactJobPeriodicTable, stoppingToken)
                    .ConfigureAwait(false);

                await EnsurePeriodicJobsAsync(db, stoppingToken)
                    .ConfigureAwait(false);

                await tx.CommitAsync(stoppingToken)
                    .ConfigureAwait(false);

                await dialect.ReleaseTableLockAsync(db, dialect.XactJobSchema, dialect.XactJobPeriodicTable, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    Logger.LogError(ex, "Failed to create periodic jobs");
                }

                try
                {
                    if (tx != null)
                    {
                        await tx.RollbackAsync(stoppingToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (Exception exx)
                {
                    Logger.LogError(exx, "Failed to rollback");
                }
            }
            finally
            {
                try
                {
                    tx?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to dispose transaction");
                }
            }
        }
    }
}

