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

namespace XactJobs.Internal
{
    internal sealed class XactJobRunner<TDbContext> where TDbContext: DbContext
    {
        private readonly string _queueName;
        private readonly QuickPollChannel _quickPollChannel;
        private readonly Guid _leaser = Guid.NewGuid();
        private readonly XactJobsOptionsBase<TDbContext> _options;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        public XactJobRunner(string queueName, QuickPollChannel quickPollChannel, XactJobsOptionsBase<TDbContext> options, IServiceScopeFactory scopeFactory, ILogger logger)
        {
            _queueName = queueName;
            _quickPollChannel = quickPollChannel;
            _options = options;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        /// <summary>
        /// Executes the job runner loop, polling for jobs and processing them in batches.
        /// Handles initial delay, polling intervals, error backoff, and graceful shutdown.
        /// </summary>
        /// <param name="stoppingToken">A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
        /// <param name="initialDelayMs">The initial delay in milliseconds before starting job polling.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
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
                        var now = DateTime.UtcNow;

                        nextRunTime = AlignNextRun(nextRunTime, delaySec, now);

                        var shouldPoll = await WaitForNextPollAsync(nextRunTime.Subtract(now), stoppingToken)
                            .ConfigureAwait(false);

                        if (!shouldPoll) continue;
                    }
                    else
                    {
                        // consume batch of notifications, since we're about to poll
                        await _quickPollChannel.ConsumeBatchAsync(_options.BatchSize, stoppingToken)
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
                await ClearLeasesAsync()
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Queue}: Failed clearing leases for leaser '{Leaser}' during shutdown", GetQueueDisplayName(), _leaser);
            }
        }

        /// <summary>
        /// This next run time calculation is meant to keep the run times consistent, in delaySec steps, 
        /// so that multiple workers do not start hitting the Db at the same time.
        /// </summary>
        /// <param name="nextRunTime"></param>
        /// <param name="delaySec"></param>
        /// <param name="now"></param>
        /// <returns></returns>
        private static DateTime AlignNextRun(DateTime nextRunTime, int delaySec, DateTime now)
        {
            // if next run time is more than 1s in the future, no need to do anything, just wait for the next poll
            if (nextRunTime >= now.AddMilliseconds(1000))
            {
                return nextRunTime;
            }

            do
            {
                nextRunTime = nextRunTime.AddSeconds(delaySec);
            }
            while (nextRunTime <= now);

            return nextRunTime;
        }

        private async Task<bool> WaitForNextPollAsync(TimeSpan waitTime, CancellationToken stoppingToken)
        {
            int consumedCount = 0;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

            cts.CancelAfter(waitTime);

            try
            {
                await _quickPollChannel.WaitToReadAsync(cts.Token)
                    .ConfigureAwait(false);

                // drain the channel, up to the batch size (we're about to poll)
                consumedCount = await _quickPollChannel.ConsumeBatchAsync(_options.BatchSize, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // we want to throw if the app is being stopped (caught outside)
                if (stoppingToken.IsCancellationRequested) throw;

                // else: timeout hit, proceed to poll
            }

            // if we waited the entire waitTime or we consumed some notifications, it's ok to poll now
            bool shouldPollNow = cts.IsCancellationRequested || consumedCount > 0;

            return shouldPollNow;
        }

        private async Task ClearLeasesAsync()
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

            using var extendLeaseTimer = new AsyncTimer(_logger, TimeSpan.FromSeconds(_options.LeaseDurationInSeconds * 0.75), ExtendLeaseAsync);

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
                    if (job.PeriodicJobId != null)
                    {
                        periodicJobs.TryGetValue(job.PeriodicJobId, out periodicJob);
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

        private async Task ExtendLeaseAsync(CancellationToken token)
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

                if (job.PeriodicJobId != null && (periodicJob == null || !periodicJob.IsActive || !periodicJob.IsCompatibleWith(job)))
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


        private void RecordSuccess(TDbContext dbContext, XactJob job, XactJobPeriodic? periodicJob)
        {
            RecordProcessingAttempt(dbContext, job, periodicJob, ProcessingResult.Completed, null);
        }

        private void RecordSkipped(TDbContext dbContext, XactJob job, XactJobPeriodic? periodicJob)
        {
            RecordProcessingAttempt(dbContext, job, periodicJob, ProcessingResult.Skipped, null);
        }

        private void RecordFailedAttempt(TDbContext dbContext, XactJob job, XactJobPeriodic? periodicJob, Exception ex)
        {
            RecordProcessingAttempt(dbContext, job, periodicJob, ProcessingResult.Failed, ex);
        }

        private void RecordProcessingAttempt(TDbContext dbContext, XactJob job, XactJobPeriodic? periodicJob, ProcessingResult processingResult, Exception? ex)
        {
            var status = processingResult switch
            {
                ProcessingResult.Completed => XactJobStatus.Completed,
                ProcessingResult.Failed => XactJobStatus.Failed,
                ProcessingResult.Skipped => XactJobStatus.Skipped,
                _ => throw new ArgumentOutOfRangeException(nameof(processingResult)),
            };

            var errorCount = job.ErrorCount;

            if (status == XactJobStatus.Failed)
            {
                errorCount++;
                var nextRunTimeUtc = _options.RetryStrategy.GetRetryTimeUtc(job, errorCount);

                if (nextRunTimeUtc.HasValue)
                {
                    dbContext.Reschedule(job, nextRunTimeUtc.Value, errorCount);
                }
                else
                {
                    status = XactJobStatus.Cancelled;
                }
            }

            dbContext.Set<XactJobHistory>().Add(XactJobHistory.CreateFromJob(job, DateTime.UtcNow, status, errorCount, ex));

            if (periodicJob != null 
                && status != XactJobStatus.Failed // only failed jobs are not re-scheduled to prevent overlap (they are re-scheduled above)
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
