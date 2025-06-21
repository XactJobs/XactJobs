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

        public async Task ExecuteAsync(CancellationToken stoppingToken, int initialDelayMs)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(initialDelayMs), stoppingToken)
                .ConfigureAwait(false);

            var parallelOptions = new ParallelOptions
            {
                CancellationToken = stoppingToken,
                MaxDegreeOfParallelism = _options.MaxDegreeOfParallelism
            };

            var lastRunFailed = 0;
            var lastRunJobCount = 0;

            var nextRunTime = DateTime.UtcNow;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var delaySec = lastRunFailed > 0
                        ? _options.PollingIntervalInSeconds * Math.Min(lastRunFailed, 5)
                        : lastRunJobCount == _options.BatchSize ? 0 : _options.PollingIntervalInSeconds;

                    if (delaySec > 0)
                    {
                        // this following run time calculation is meant to keep
                        // the run times consistent, in delaySec steps, so that multiple workers
                        // do not start hitting the db at the same time

                        var now = DateTime.UtcNow;
                        do
                        {
                            nextRunTime = nextRunTime.AddSeconds(delaySec);
                        }
                        while (nextRunTime <= now);

                        await Task.Delay(nextRunTime.Subtract(now), stoppingToken)
                            .ConfigureAwait(false);
                    }

                    lastRunJobCount = await RunJobsAsync(parallelOptions, stoppingToken)
                        .ConfigureAwait(false);

                    lastRunFailed = 0;
                }
                catch (Exception ex)
                {
                    lastRunFailed++;

                    if (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "{Queue}: Processing jobs failed. Retrying in {RetryIn} seconds",
                            GetQueueDisplayName(), _options.PollingIntervalInSeconds * Math.Min(lastRunFailed, 5));
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

        private async Task<int> RunJobsAsync(ParallelOptions parallelOptions, CancellationToken stoppingToken)
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

            var periodicJobIds = jobs.Select(job => job.PeriodicJobId).ToList();

            var periodicJobs = periodicJobIds.Count > 0
                ? await dbContext.Set<XactJobPeriodic>()
                    .Where(x => periodicJobIds.Contains(x.Id))
                    .AsNoTracking()
                    .ToDictionaryAsync(x => x.Id, stoppingToken)
                    .ConfigureAwait(false)
                : [];

            await Parallel.ForEachAsync(jobs, parallelOptions, async (job, stoppingToken) =>
            {
                XactJobPeriodic? periodicJob = null;
                try
                {
                    if (job.PeriodicJobId.HasValue)
                    {
                        periodicJobs.TryGetValue(job.PeriodicJobId.Value, out periodicJob);
                    }
                    
                    await RunJobAsync(job, periodicJob, stoppingToken)
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

                    RecordFailedAttempt(dbContext, job, periodicJob, ex);
                }
            })
                .ConfigureAwait(false);

            await dbContext.SaveChangesAsync(stoppingToken)
                .ConfigureAwait(false);

            await extendLeaseTimer.StopAsync()
                .ConfigureAwait(false);

            return jobs.Count;
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

        public async Task RunJobAsync(XactJob job, XactJobPeriodic? periodicJob, CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            try
            {
                dbContext.Attach(job);

                if (job.PeriodicJobId.HasValue && (periodicJob == null || !periodicJob.IsActive || !periodicJob.IsCompatibleWith(job)))
                {
                    // periodic job is inactive or deleted
                    RecordSkipped(dbContext, job, periodicJob);
                }
                else
                {
                    if (periodicJob != null)
                    {
                        // this is so we can detect if the job is deleted inside the job
                        dbContext.Attach(periodicJob);
                    }

                    await XactJobCompiler.CompileAndRunJobAsync(scope, job, stoppingToken)
                        .ConfigureAwait(false);

                    RecordSuccess(dbContext, job, periodicJob);
                }

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


        private static void RecordSuccess(TDbContext dbContext, XactJob job, XactJobPeriodic? periodicJob)
        {
            RecordProcessingAttempt(dbContext, job, periodicJob, ProcessingResult.Completed, null);
        }

        private static void RecordSkipped(TDbContext dbContext, XactJob job, XactJobPeriodic? periodicJob)
        {
            RecordProcessingAttempt(dbContext, job, periodicJob, ProcessingResult.Skipped, null);
        }

        private static void RecordFailedAttempt(TDbContext dbContext, XactJob job, XactJobPeriodic? periodicJob, Exception ex)
        {
            RecordProcessingAttempt(dbContext, job, periodicJob, ProcessingResult.Failed, ex);
        }

        private static int[] _retrySeconds = [2, 2, 5, 10, 30, 60, 5 * 60, 15 * 60, 30 * 60, 60 * 60];

        private static void RecordProcessingAttempt(TDbContext dbContext, XactJob job, XactJobPeriodic? periodicJob, ProcessingResult processingResult, Exception? ex)
        {
            var status = processingResult switch
            {
                ProcessingResult.Completed => XactJobStatus.Completed,
                ProcessingResult.Failed => XactJobStatus.Failed,
                ProcessingResult.Skipped => XactJobStatus.Skipped,
                _ => throw new ArgumentOutOfRangeException(nameof(processingResult)),
            };

            if (status == XactJobStatus.Failed)
            {
                // incompatible periodic jobs will never get here (they will be skipped)
                job.MarkFailed();

                // TODO: implement configurable retry strategy
                if (job.ErrorCount < 10)
                {
                    var seconds = job.ErrorCount <= _retrySeconds.Length 
                        ? _retrySeconds[job.ErrorCount - 1] 
                        : _retrySeconds[^1];

                    dbContext.Reschedule(job, DateTime.UtcNow.AddSeconds(seconds));
                }
                else
                {
                    status = XactJobStatus.Cancelled;
                }
            }

            dbContext.Set<XactJobHistory>().Add(XactJobHistory.CreateFromJob(job, periodicJob, DateTime.UtcNow, status, ex));

            if (periodicJob != null 
                && (status == XactJobStatus.Completed || status == XactJobStatus.Skipped)
                && periodicJob.IsCompatibleWith(job) 
                && dbContext.Entry(periodicJob).State != EntityState.Deleted)
            {
                dbContext.ScheduleNextRun(periodicJob);
            }

            dbContext.Set<XactJob>().Remove(job);
        }

        private string GetQueueDisplayName()
        {
            return _queueName ?? "Default";
        }

        private enum ProcessingResult
        {
            Completed,
            Failed,
            Skipped
        }
    }
}
