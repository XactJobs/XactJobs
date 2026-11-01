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
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace XactJobs.Internal
{
    internal sealed class XactJobRunner<TDbContext> where TDbContext: DbContext
    {
        // How long jobs get to react to the cancellation, after the shutdown timeout has passed
        private static readonly TimeSpan JobCancellationTimeout = TimeSpan.FromSeconds(5);

        // Jobs claimed by this runner and not finished yet, by primary key.
        // Jobs that were cancelled because of a shutdown stay here, so their leases get cleared.
        private readonly ConcurrentDictionary<(string Queue, DateTime ScheduledAt, long Id), XactJob> _inFlightJobs = new();

        // Signals that a job task has finished. It doesn't count them: a reader has to check the state it's waiting for.
        private readonly Channel<bool> _jobFinished = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });

        // The most jobs in flight at the same time: never claim more than can start running immediately
        private readonly int _capacity;

        private int _runningJobCount;

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

            var maxDegreeOfParallelism = options.MaxDegreeOfParallelism > 0 ? options.MaxDegreeOfParallelism : Environment.ProcessorCount;

            _capacity = Math.Max(1, Math.Min(options.BatchSize, maxDegreeOfParallelism));
        }

        /// <summary>
        /// Executes the job runner loop, polling for jobs to fill the free slots and starting each of them as its own task.
        /// Handles initial delay, polling intervals, error backoff, and graceful shutdown.
        /// </summary>
        /// <param name="stoppingToken">A <see cref="CancellationToken"/> to observe while waiting for the task to complete.</param>
        /// <param name="initialDelayMs">The initial delay in milliseconds before starting job polling.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        public async Task ExecuteAsync(CancellationToken stoppingToken, int initialDelayMs)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(initialDelayMs), stoppingToken)
                .ConfigureAwait(false);

            // not disposed: a job ignoring the cancellation can still be using the token after this method returns
            var jobsCts = new CancellationTokenSource();

            using var extendLeaseTimer = new AsyncTimer(_logger, TimeSpan.FromSeconds(_options.LeaseDurationInSeconds * 0.75), ExtendLeaseAsync);

            extendLeaseTimer.Start();

            await PollUntilStoppedAsync(jobsCts.Token, stoppingToken)
                .ConfigureAwait(false);

            await WaitForRunningJobsOnShutdownAsync(jobsCts)
                .ConfigureAwait(false);

            await extendLeaseTimer.StopAsync()
                .ConfigureAwait(false);

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

        private async Task PollUntilStoppedAsync(CancellationToken jobsToken, CancellationToken stoppingToken)
        {
            var failedPolls = 0;
            var lastPollFilledSlots = false;

            var nextRunTime = DateTime.UtcNow;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (failedPolls == 0 && lastPollFilledSlots)
                    {
                        // likely a backlog: no need to wait for the interval, just for a free slot
                        await WaitForFreeSlotAsync(stoppingToken)
                            .ConfigureAwait(false);

                        // consume batch of notifications, since we're about to poll
                        await _quickPollChannel.ConsumeBatchAsync(_options.BatchSize, stoppingToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        var delaySec = failedPolls > 0
                            ? _options.PollingIntervalInSeconds * Math.Min(failedPolls, 5)
                            : _options.PollingIntervalInSeconds;

                        var now = DateTime.UtcNow;

                        nextRunTime = AlignNextRun(nextRunTime, delaySec, now);

                        var shouldPoll = await WaitForNextPollAsync(nextRunTime.Subtract(now), stoppingToken)
                            .ConfigureAwait(false);

                        if (!shouldPoll) continue;
                    }

                    var freeSlots = GetFreeSlots();

                    if (freeSlots <= 0)
                    {
                        // skip the database, there's nothing to do until a slot frees up
                        lastPollFilledSlots = true;
                        continue;
                    }

                    var claimedCount = await ClaimAndStartJobsAsync(freeSlots, jobsToken, stoppingToken)
                        .ConfigureAwait(false);

                    lastPollFilledSlots = claimedCount == freeSlots;

                    failedPolls = 0;
                }
                catch (Exception ex)
                {
                    failedPolls++;

                    if (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "{Queue}: Processing jobs failed. Retrying in {RetryIn} seconds",
                            GetQueueDisplayName(), _options.PollingIntervalInSeconds * Math.Min(failedPolls, 5));
                    }
                }
            }
        }

        private int GetFreeSlots()
        {
            return _capacity - _inFlightJobs.Count;
        }

        private async Task WaitForFreeSlotAsync(CancellationToken stoppingToken)
        {
            while (GetFreeSlots() <= 0)
            {
                await _jobFinished.Reader.ReadAsync(stoppingToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Gives the running jobs the time to finish, then cancels the ones that didn't.
        /// The leases keep being extended meanwhile.
        /// </summary>
        private async Task WaitForRunningJobsOnShutdownAsync(CancellationTokenSource jobsCts)
        {
            var finished = await WaitForNoRunningJobsAsync(TimeSpan.FromSeconds(_options.ShutdownTimeoutInSeconds))
                .ConfigureAwait(false);

            if (finished) return;

            await jobsCts.CancelAsync()
                .ConfigureAwait(false);

            finished = await WaitForNoRunningJobsAsync(JobCancellationTimeout)
                .ConfigureAwait(false);

            if (!finished)
            {
                _logger.LogWarning("{Queue}: {Count} jobs did not stop after being cancelled. Their leases will be cleared, so they can run again on another worker",
                    GetQueueDisplayName(), Volatile.Read(ref _runningJobCount));
            }
        }

        private async Task<bool> WaitForNoRunningJobsAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);

            try
            {
                while (Volatile.Read(ref _runningJobCount) > 0)
                {
                    await _jobFinished.Reader.ReadAsync(cts.Token)
                        .ConfigureAwait(false);
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                return Volatile.Read(ref _runningJobCount) == 0;
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
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ClearLeaseTimeoutInSeconds));

            await ExecuteLeaseSqlForInFlightJobsAsync((dialect, jobs) => dialect.GetClearLeaseSql(_leaser, jobs), cts.Token)
                .ConfigureAwait(false);
        }

        private async Task ExecuteLeaseSqlForInFlightJobsAsync(Func<ISqlDialect, XactJob[], (string Sql, object[] Parameters)> getLeaseSql, CancellationToken token)
        {
            var jobs = _inFlightJobs.Values; // this is a snapshot

            if (jobs.Count == 0) return;

            using var scope = _scopeFactory.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            var dialect = dbContext.Database.ProviderName.ToSqlDialect();

            foreach (var chunk in jobs.Chunk(SqlDialectExtensions.MaxJobsPerStatement))
            {
                var (sql, parameters) = getLeaseSql(dialect, chunk);

                await dbContext.Database.ExecuteSqlRawAsync(sql, parameters, token)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The scope is used only for the claim: each job runs, and records a failure, in its own scope.
        /// </summary>
        private async Task<int> ClaimAndStartJobsAsync(int maxJobs, CancellationToken jobsToken, CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            var dialect = dbContext.Database.ProviderName.ToSqlDialect();

            var jobs = await dialect.ClaimJobsAsync(dbContext, _queueName, maxJobs, _leaser, _options.LeaseDurationInSeconds, stoppingToken)
                .ConfigureAwait(false);

            foreach (var job in jobs)
            {
                _inFlightJobs[GetKey(job)] = job;
            }

            Dictionary<string, XactJobPeriodic> periodicJobs;

            try
            {
                periodicJobs = await LoadPeriodicJobsAsync(dbContext, jobs, stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested)
            {
                // these jobs won't run, so the slots are free and the leases shouldn't be extended: they will expire
                foreach (var job in jobs)
                {
                    _inFlightJobs.TryRemove(GetKey(job), out _);
                }

                throw;
            }

            foreach (var job in jobs)
            {
                XactJobPeriodic? periodicJob = null;

                if (job.PeriodicJobId != null)
                {
                    periodicJobs.TryGetValue(job.PeriodicJobId, out periodicJob);
                }

                Interlocked.Increment(ref _runningJobCount);

                // Task.Run, because a job can block for a long time before its first await, and this is the polling loop
                _ = Task.Run(() => RunClaimedJobAsync(job, periodicJob, jobsToken), CancellationToken.None);
            }

            return jobs.Count;
        }

        private static async Task<Dictionary<string, XactJobPeriodic>> LoadPeriodicJobsAsync(TDbContext dbContext, List<XactJob> jobs, CancellationToken cancellationToken)
        {
            var periodicJobIds = jobs
                .Where(job => job.PeriodicJobId != null)
                .Select(job => job.PeriodicJobId!)
                .Distinct()
                .ToList();

            if (periodicJobIds.Count == 0) return [];

            return await dbContext.Set<XactJobPeriodic>()
                .Where(x => periodicJobIds.Contains(x.Id))
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Nothing observes this task, so it must not throw.
        /// </summary>
        private async Task RunClaimedJobAsync(XactJob job, XactJobPeriodic? periodicJob, CancellationToken jobsToken)
        {
            try
            {
                await RunJobAsync(job, periodicJob, jobsToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Queue}: Job failed: {TypeName}.{MethodName} ({Id})", GetQueueDisplayName(), job.TypeName, job.MethodName, job.Id);

                await RecordFailureAsync(job, periodicJob, ex)
                    .ConfigureAwait(false);

                _inFlightJobs.TryRemove(GetKey(job), out _);
            }
            finally
            {
                Interlocked.Decrement(ref _runningJobCount);

                _jobFinished.Writer.TryWrite(true);
            }
        }

        private static (string Queue, DateTime ScheduledAt, long Id) GetKey(XactJob job)
        {
            return (job.Queue, job.ScheduledAt, job.Id);
        }

        /// <summary>
        /// Records a failed attempt in its own scope, after the job's scope has been rolled back and disposed.
        /// Never throws: if the record can't be saved, the lease expires and the job runs again.
        /// The stopping token isn't used, so that a job failing during shutdown still gets its failure recorded.
        /// </summary>
        private async Task RecordFailureAsync(XactJob job, XactJobPeriodic? periodicJob, Exception jobException)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ClearLeaseTimeoutInSeconds));

                dbContext.Attach(job);

                RecordFailedAttempt(dbContext, job, periodicJob, jobException);

                await dbContext.SaveChangesAsync(cts.Token)
                    .ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "{Queue}: Job row is already gone, failure not recorded (lease expired and another worker may have processed it): {TypeName}.{MethodName} ({Id})",
                    GetQueueDisplayName(), job.TypeName, job.MethodName, job.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Queue}: Failed recording job failure, the job will run again when its lease expires: {TypeName}.{MethodName} ({Id})",
                    GetQueueDisplayName(), job.TypeName, job.MethodName, job.Id);
            }
        }

        private Task ExtendLeaseAsync(CancellationToken token)
        {
            return ExecuteLeaseSqlForInFlightJobsAsync((dialect, jobs) => dialect.GetExtendLeaseSql(_leaser, jobs, _options.LeaseDurationInSeconds), token);
        }

        public async Task RunJobAsync(XactJob job, XactJobPeriodic? periodicJob, CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            try
            {
                dbContext.Attach(job);

                var isSkipped = job.PeriodicJobId != null && (periodicJob == null || !periodicJob.IsActive || !periodicJob.IsCompatibleWith(job));

                if (isSkipped)
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

                    // detached means the job deleted its own periodic job and saved that, so there's no next run to schedule
                    var isPeriodicJobDeleted = periodicJob != null && dbContext.Entry(periodicJob).State == EntityState.Detached;

                    RecordSuccess(dbContext, job, isPeriodicJobDeleted ? null : periodicJob);
                }

                await dbContext.SaveChangesAsync(stoppingToken)
                    .ConfigureAwait(false);

                if (dbContext.Database.CurrentTransaction != null)
                {
                    await dbContext.Database.CurrentTransaction.CommitAsync(stoppingToken)
                        .ConfigureAwait(false);
                }

                _inFlightJobs.TryRemove(GetKey(job), out _);

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("{Queue}: Job {Outcome} - {TypeName}.{MethodName} ({Id})", GetQueueDisplayName(), isSkipped ? "skipped" : "completed", job.TypeName, job.MethodName, job.Id);
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

                _logger.LogDebug("{Queue}: Job cancelled by shutdown, it will run again - {TypeName}.{MethodName} ({Id})", GetQueueDisplayName(), job.TypeName, job.MethodName, job.Id);
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
