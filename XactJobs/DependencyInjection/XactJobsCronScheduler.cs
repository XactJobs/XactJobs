using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XactJobs.SqlDialects;

namespace XactJobs.DependencyInjection
{
    public abstract class XactJobsCronScheduler<TDbContext> : BackgroundService where TDbContext : DbContext
    {
        protected ILogger<XactJobsCronScheduler<TDbContext>> Logger { get; }
        protected IServiceScopeFactory ScopeFactory { get; }

        protected abstract Task EnsurePeriodicJobs(TDbContext db, CancellationToken stoppingToken);

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

                if (dialect is MySqlDialect)
                {
                    var result = await db.ExecuteScalarIntAsync(dialect.GetLockJobPeriodicSql(), stoppingToken)
                        .ConfigureAwait(false);

                    if (result != 1) throw new Exception($"Failed to acquire lock (Result={result})");
                }
                else if (dialect is SqlServerDialect)
                {
                    var result = await db.ExecuteOutputIntAsync(dialect.GetLockJobPeriodicSql(), stoppingToken)
                        .ConfigureAwait(false);

                    if (result < 0) throw new Exception($"Failed to acquire lock (Result={result})");
                }
                else
                {
                    await db.Database.ExecuteSqlRawAsync(dialect.GetLockJobPeriodicSql(), stoppingToken)
                        .ConfigureAwait(false);
                }

                await EnsurePeriodicJobs(db, stoppingToken)
                    .ConfigureAwait(false);

                await tx.CommitAsync(stoppingToken)
                    .ConfigureAwait(false);

                if (dialect is MySqlDialect)
                {
                    await db.ExecuteScalarIntAsync("RELEASE_ALL_LOCKS()", stoppingToken)
                        .ConfigureAwait(false);
                }
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

