using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XactJobs
{
    public sealed class XactJobRunner<TDbContext> where TDbContext: DbContext
    {
        private readonly string? _queueName;
        private readonly Guid _leaser = Guid.NewGuid();
        private readonly XactJobsOptionsBase<TDbContext> _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        public XactJobRunner(string? queueName, XactJobsOptionsBase<TDbContext> options, IServiceScopeFactory scopeFactory, ILogger logger)
        {
            _queueName = queueName;
            _options = options;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var parallelOptions = new ParallelOptions
            {
                CancellationToken = stoppingToken,
                MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism
            };

            var lastRunFailed = false;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var delaySec = lastRunFailed 
                        ? _options.WorkerErrorRetryDelayInSeconds 
                        : _options.PollingIntervalInSeconds;

                    lastRunFailed = false;

                    await Task.Delay(TimeSpan.FromSeconds(delaySec), stoppingToken)
                        .ConfigureAwait(false);

                    await RunJobsAsync(parallelOptions, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    lastRunFailed = true;

                    if (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "{Queue}: Processing jobs failed. Retrying in {RetryIn} seconds", GetQueueDisplayName(), _options.WorkerErrorRetryDelayInSeconds);
                    }
                }
            }

            try
            {
                await ClearLeases();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Queue}: Failed clearing leases for leaser '{Leaser}' during shutdown", GetQueueDisplayName(), _leaser);
            }
        }

        private async Task ClearLeases()
        {
            using var scope = _scopeFactory.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            var dialect = dbContext.Database.ProviderName.ToSqlDialect();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ClearLeaseTimeoutInSeconds));

            await dbContext.Database.ExecuteSqlRawAsync(dialect.GetClearLeaseSql(_leaser), cts.Token)
                .ConfigureAwait(false);
        }

        private async Task RunJobsAsync(ParallelOptions parallelOptions, CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            var dialect = dbContext.Database.ProviderName.ToSqlDialect();

            var acquireLeaseSql = dialect.GetAcquireLeaseSql(_queueName, _options.BatchSize, _leaser, _options.LeaseDurationInSeconds);
            var fetchJobsSql = dialect.GetFetchJobsSql(_queueName, _options.BatchSize, _leaser, _options.LeaseDurationInSeconds);

            using var extendLeaseTimer = new AsyncTimer(_logger, TimeSpan.FromSeconds(_options.LeaseDurationInSeconds * 0.75), ExtendLease);

            extendLeaseTimer.Start();

            if (acquireLeaseSql != null)
            {
                await dbContext.Database.ExecuteSqlRawAsync(acquireLeaseSql, stoppingToken)
                    .ConfigureAwait(false);
            }

            var jobs = await dbContext.Set<XactJob>().FromSqlRaw(fetchJobsSql)
                .AsNoTracking()
                .ToListAsync(stoppingToken)
                .ConfigureAwait(false);

            await Parallel.ForEachAsync(jobs, parallelOptions, async (job, stoppingToken) =>
            {
                try
                {
                    await RunJobAsync(job, stoppingToken)
                        .ConfigureAwait(false);

                    if (_logger.IsEnabled(LogLevel.Debug))
                    {
                        _logger.LogDebug("{Queue}: Job completed - {TypeName}.{MethodName} ({Id})", GetQueueDisplayName(), job.TypeName, job.MethodName, job.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{Queue}: Job failed: {TypeName}.{MethodName} ({Id})", GetQueueDisplayName(), job.TypeName, job.MethodName, job.Id);

                    dbContext.Attach(job);

                    job.MarkFailed(ex);

                    if (job.Status == XactJobStatus.Cancelled)
                    {
                        ArchiveJob(dbContext, job);
                    }
                }
            })
                .ConfigureAwait(false);

            await dbContext.SaveChangesAsync(stoppingToken)
                .ConfigureAwait(false);

            await extendLeaseTimer.StopAsync()
                .ConfigureAwait(false);
        }

        private async Task ExtendLease(CancellationToken token)
        {
            using var scope = _scopeFactory.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            var dialect = dbContext.Database.ProviderName.ToSqlDialect();

            var extendLeaseSql = dialect.GetExtendLeaseSql(_leaser, _options.LeaseDurationInSeconds);

            await dbContext.Database.ExecuteSqlRawAsync(extendLeaseSql, token)
                .ConfigureAwait(false);
        }

        public async Task RunJobAsync(XactJob job, CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            try
            {
                dbContext.Attach(job);

                await XactJobCompiler.CompileAndRunJobAsync(scope, job, stoppingToken)
                    .ConfigureAwait(false);

                job.MarkCompleted();

                ArchiveJob(dbContext, job);

                await dbContext.SaveChangesAsync(stoppingToken)
                    .ConfigureAwait(false);

                if (dbContext.Database.CurrentTransaction != null)
                {
                    await dbContext.Database.CurrentTransaction.CommitAsync(stoppingToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (dbContext.Database.CurrentTransaction != null)
                {
                    // try to roll back always, even if stopping
                    await dbContext.Database.CurrentTransaction.RollbackAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }

                if (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
            }
            finally
            {
                if (dbContext.Database.CurrentTransaction != null)
                {
                    await dbContext.Database.CurrentTransaction.DisposeAsync()
                        .ConfigureAwait(false);
                }
            }
        }

        private static void ArchiveJob(TDbContext dbContext, XactJob job)
        {
            dbContext.Set<XactJobArchive>().Add(XactJobArchive.CreateFromJob(job, DateTime.UtcNow));
            dbContext.Set<XactJob>().Remove(job);
        }

        private string GetQueueDisplayName()
        {
            return _queueName ?? "Default";
        }
    }
}
