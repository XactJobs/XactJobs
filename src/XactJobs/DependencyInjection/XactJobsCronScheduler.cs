using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

            var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

            var dialect = db.Database.ProviderName.ToSqlDialect();

            IDbContextTransaction? tx = null; 
            try
            {
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
                tx?.Dispose();
            }
        }
    }
}

