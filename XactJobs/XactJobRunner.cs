using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace XactJobs
{
    public sealed class XactJobRunner<TDbContext> where TDbContext: DbContext
    {
        private readonly string? _queueName;
        private readonly Guid _leaser = Guid.NewGuid();
        private readonly XactJobsOptions _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        public XactJobRunner(string? queueName, XactJobsOptions options, IServiceScopeFactory scopeFactory, ILogger logger)
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
            
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalInSeconds), stoppingToken)
                        .ConfigureAwait(false);

                    await RunJobsAsync(parallelOptions, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Running Jobs failed. Retrying in 10 seconds");

                    await Task.Delay(TimeSpan.FromSeconds(_options.WorkerErrorRetryDelayInSeconds), stoppingToken);
                }
            }

            try
            {
                await ClearLeases();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed clearing leases for leaser '{Leaser}' during shutdown", _leaser);
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

            var extendLeaseSql = dialect.GetExtendLeaseSql(_leaser, _options.LeaseDurationInSeconds);
            var acquireLeaseSql = dialect.GetAcquireLeaseSql(_queueName, _options.BatchSize, _leaser, _options.LeaseDurationInSeconds);
            var fetchJobsSql = dialect.GetFetchJobsSql(_queueName, _options.BatchSize, _leaser, _options.LeaseDurationInSeconds);

            using var extendLeaseTimer = new AsyncTimer(_logger, TimeSpan.FromSeconds(_options.LeaseDurationInSeconds * 0.75), async token =>
            {
                await dbContext.Database.ExecuteSqlRawAsync(extendLeaseSql, token)
                    .ConfigureAwait(false);
            });

            extendLeaseTimer.Start();

            if (acquireLeaseSql != null)
            {
                await dbContext.Database.ExecuteSqlRawAsync(acquireLeaseSql, stoppingToken)
                    .ConfigureAwait(false);
            }

            var jobs = await dbContext.Set<XactJob>().FromSqlRaw(fetchJobsSql)
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
                        _logger.LogDebug("Job completed: {TypeName}.{MethodName} ({Id})", job.TypeName, job.MethodName, job.Id);
                    }
                }
                catch (Exception ex)
                {
                    job.MarkFailed(ex);
                    _logger.LogError(ex, "Job failed: {TypeName}.{MethodName} ({Id})", job.TypeName, job.MethodName, job.Id);
                }
            })
                .ConfigureAwait(false);

            await dbContext.SaveChangesAsync(stoppingToken)
                .ConfigureAwait(false);

            await extendLeaseTimer.StopAsync()
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

                await dbContext.SaveChangesAsync(stoppingToken)
                    .ConfigureAwait(false);

                if (dbContext.Database.CurrentTransaction != null)
                {
                    await dbContext.Database.CurrentTransaction.CommitAsync(stoppingToken)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                if (dbContext.Database.CurrentTransaction != null)
                {
                    await dbContext.Database.CurrentTransaction.RollbackAsync(stoppingToken)
                        .ConfigureAwait(false);
                }
                throw;
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
    }
}
