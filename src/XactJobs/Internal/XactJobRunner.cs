// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;

namespace XactJobs.Internal
{
    internal sealed class XactJobRunner<TDbContext> : IXactJobsWorkerState where TDbContext: DbContext
    {
        // How long jobs get to react to the cancellation, after the shutdown timeout has passed
        private static readonly TimeSpan JobCancellationTimeout = TimeSpan.FromSeconds(5);

        // During a backlog, how long a worker with a batch waits for the batch to finish before it claims again, while fewer jobs
        // wait than it can run
        private static readonly TimeSpan EarlyClaimDelay = TimeSpan.FromSeconds(1);

        // Cancels the wait for EarlyClaimDelay, or when the worker stops. Used only by the polling loop
        private CancellationTokenSource? _earlyClaimCts;

        // Jobs claimed by this runner and not finished yet, by primary key.
        // Jobs that were cancelled because of a shutdown stay here, so their leases get cleared.
        private readonly ConcurrentDictionary<(string Queue, DateTime ScheduledAt, long Id), XactJob> _inFlightJobs = new();

        // Signals that a job task has finished. It doesn't count them: a reader has to check the state it's waiting for.
        private readonly Channel<bool> _jobFinished = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite
        });

        // The most jobs in flight (claimed, and not finished) at the same time: the batch size, by default twice the parallelism.
        // At or below the parallelism, a worker only claims jobs it can start immediately
        private readonly int _capacity;

        // The most jobs running at the same time
        private readonly int _maxDegreeOfParallelism;

        // With a batch larger than the parallelism, the claimed jobs wait here for a slot to run, in no set order. Null otherwise.
        private readonly SemaphoreSlim? _runSlots;

        private int _runningJobCount;

        private readonly string _queueName;
        private readonly QuickPollChannel _quickPollChannel;
        private readonly QuickPollChannels _quickPollChannels;
        private readonly Guid _leaser = Guid.NewGuid();
        private readonly XactJobsOptionsBase<TDbContext> _options;
        private readonly XactJobSaveChecker _saveChecker;
        private readonly XactJobsMetrics _metrics;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger _logger;

        // continuations are claimed at most once per polling interval, unless a finished batch wakes the queue
        private DateTime _nextContinuationClaimAt = DateTime.MinValue;

        public XactJobRunner(string queueName,
                             QuickPollChannel quickPollChannel,
                             QuickPollChannels quickPollChannels,
                             XactJobsOptionsBase<TDbContext> options,
                             XactJobSaveChecker saveChecker,
                             XactJobsMetrics metrics,
                             IServiceScopeFactory scopeFactory,
                             ILogger logger)
        {
            _queueName = queueName;
            _quickPollChannel = quickPollChannel;
            _quickPollChannels = quickPollChannels;
            _options = options;
            _saveChecker = saveChecker;
            _metrics = metrics;
            _scopeFactory = scopeFactory;
            _logger = logger;

            _maxDegreeOfParallelism = options.MaxDegreeOfParallelism > 0 ? options.MaxDegreeOfParallelism : Environment.ProcessorCount;
            // by default twice the parallelism: each slot has a job waiting, so it doesn't wait for a claim when its job finishes
            _capacity = options.BatchSize > 0 ? Math.Max(options.BatchSize, _maxDegreeOfParallelism) : 2 * _maxDegreeOfParallelism;

            if (_capacity > _maxDegreeOfParallelism)
            {
                _runSlots = new SemaphoreSlim(_maxDegreeOfParallelism, _maxDegreeOfParallelism);
            }
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

            // its running and held jobs, in the metrics
            using var metricsRegistration = XactJobsMetrics.Register(this);

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
                        await _quickPollChannel.ConsumeBatchAsync(_capacity, stoppingToken)
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

                    // when stopping, a cancelled command can also throw the provider's own exception (SqlClient's SqlException)
                    if (!stoppingToken.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "{Queue}: Processing jobs failed. Retrying in {RetryIn} seconds",
                            GetQueueDisplayName(), _options.PollingIntervalInSeconds * Math.Min(failedPolls, 5));
                    }
                }
            }

            _earlyClaimCts?.Dispose();
            _earlyClaimCts = null;
        }

        private int GetFreeSlots()
        {
            return _capacity - _inFlightJobs.Count;
        }

        /// <summary>
        /// During a backlog: when a slot is free. With a batch larger than the parallelism, only once fewer jobs wait than can run,
        /// so that the worker claims a batch, not one job each time one finishes. Then it still waits for the batch to finish, at
        /// most <see cref="EarlyClaimDelay"/> (see <see cref="WaitForFreeSlotAsync"/>).
        /// </summary>
        private bool ShouldClaimMore()
        {
            if (GetFreeSlots() <= 0) return false;

            if (_runSlots == null) return true;

            var waiting = _inFlightJobs.Count - Volatile.Read(ref _runningJobCount);

            return waiting < _maxDegreeOfParallelism;
        }

        private async Task WaitForFreeSlotAsync(CancellationToken stoppingToken)
        {
            while (!ShouldClaimMore())
            {
                await _jobFinished.Reader.ReadAsync(stoppingToken)
                    .ConfigureAwait(false);
            }

            if (_runSlots == null || _inFlightJobs.IsEmpty) return;

            // A batch: the claim waits for the batch to finish, so that it doesn't run while the batch's jobs save their results
            // (on SQLite, one writer at a time: a claim that collides with a save waits 150 ms in Microsoft.Data.Sqlite's retry).
            // At most EarlyClaimDelay, so that a slow job doesn't leave the other slots idle.
            // One token source for every wait, reset after each: a new one only when the delay has passed (or the worker stops)
            _earlyClaimCts ??= CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

            _earlyClaimCts.CancelAfter(EarlyClaimDelay);

            try
            {
                while (!_inFlightJobs.IsEmpty)
                {
                    await _jobFinished.Reader.ReadAsync(_earlyClaimCts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // the delay has passed: claim for the free slots
            }
            finally
            {
                // also stops the delay's timer
                if (!_earlyClaimCts.TryReset())
                {
                    _earlyClaimCts.Dispose();
                    _earlyClaimCts = null;
                }
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

                // drain the channel, up to the capacity (we're about to poll)
                consumedCount = await _quickPollChannel.ConsumeBatchAsync(_capacity, stoppingToken)
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

            var dialect = dbContext.GetSqlDialect();

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

            var dialect = dbContext.GetSqlDialect();

            // the dialects' lists are new for each claim: the first one is kept, not copied
            List<XactJob>? jobs = null;

            // in whole microseconds, which every database stores exactly: the continuation claim saves it as their original time
            var claimedAt = TruncateToMicroseconds(DateTime.UtcNow);

            var claimStartedAt = Stopwatch.GetTimestamp();

            if (ShouldClaimContinuations())
            {
                // first, so that a backlog of the queue doesn't hold them back
                jobs = await dialect.ClaimContinuationsAsync(dbContext, _queueName, maxJobs, _leaser, _options.LeaseDurationInSeconds, claimedAt, stoppingToken)
                    .ConfigureAwait(false);

                if (jobs.Count == maxJobs)
                {
                    // there may be more of them
                    _nextContinuationClaimAt = DateTime.MinValue;
                }
            }

            ExceptionDispatchInfo? claimError = null;

            var continuationCount = jobs?.Count ?? 0;

            if (continuationCount < maxJobs)
            {
                try
                {
                    var claimed = await dialect.ClaimJobsAsync(dbContext, _queueName, maxJobs - continuationCount, _leaser, _options.LeaseDurationInSeconds, stoppingToken)
                        .ConfigureAwait(false);

                    if (jobs == null || jobs.Count == 0)
                    {
                        jobs = claimed;
                    }
                    else
                    {
                        jobs.AddRange(claimed);
                    }
                }
                catch (Exception ex) when (continuationCount > 0)
                {
                    // the continuations are leased already: start them, then report the failure
                    claimError = ExceptionDispatchInfo.Capture(ex);
                }
            }

            // set by one of the claims: no continuations means that the jobs' claim ran, and if it threw, so does this method
            jobs ??= [];

            XactJobsMetrics.RecordClaim(GetQueueDisplayName(), jobs.Count, Stopwatch.GetElapsedTime(claimStartedAt));

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

                // with a batch, it counts as running once it has a slot to run (see TakeRunSlotAsync)
                if (_runSlots == null)
                {
                    Interlocked.Increment(ref _runningJobCount);
                }

                // Task.Run, because a job can block for a long time before its first await, and this is the polling loop
                _ = Task.Run(() => RunClaimedJobAsync(job, periodicJob, claimedAt, jobsToken, stoppingToken), CancellationToken.None);
            }

            claimError?.Throw();

            return jobs.Count;
        }

        private bool ShouldClaimContinuations()
        {
            var now = DateTime.UtcNow;

            var isWokenUp = _quickPollChannel.TakeContinuationsReady();

            if (!isWokenUp && now < _nextContinuationClaimAt) return false;

            _nextContinuationClaimAt = now.AddSeconds(_options.PollingIntervalInSeconds);

            return true;
        }

        // read only: the jobs look up their periodic job in it
        private static readonly Dictionary<string, XactJobPeriodic> NoPeriodicJobs = [];

        private static async Task<Dictionary<string, XactJobPeriodic>> LoadPeriodicJobsAsync(TDbContext dbContext, List<XactJob> jobs, CancellationToken cancellationToken)
        {
            // without LINQ, and nothing new when none of the jobs is periodic (most claims). A list, which EF translates: a claim has
            // few periodic jobs, so the duplicates check is cheap
            List<string>? periodicJobIds = null;

            foreach (var job in jobs)
            {
                if (job.PeriodicJobId != null && periodicJobIds?.Contains(job.PeriodicJobId) != true)
                {
                    (periodicJobIds ??= []).Add(job.PeriodicJobId);
                }
            }

            if (periodicJobIds == null) return NoPeriodicJobs;

            return await dbContext.Set<XactJobPeriodic>()
                .Where(x => periodicJobIds.Contains(x.Id))
                .AsNoTracking()
                .ToDictionaryAsync(x => x.Id, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Nothing observes this task, so it must not throw.
        /// </summary>
        private async Task RunClaimedJobAsync(XactJob job, XactJobPeriodic? periodicJob, DateTime claimedAt, CancellationToken jobsToken, CancellationToken stoppingToken)
        {
            // the worker is stopping: it stays in flight, so its lease is cleared, and another worker can claim it right away
            if (_runSlots != null && !await TakeRunSlotAsync(stoppingToken).ConfigureAwait(false)) return;

            var queue = GetQueueDisplayName();

            // the job's properties on every entry logged while it runs, the job's own and EF's too
            using var logScope = _logger.BeginScope(new XactJobLogScope(job, queue));

            var startedAt = Stopwatch.GetTimestamp();

            // due at its scheduled time (a retry's too), or, for a continuation that waited for its batch, when it was claimed
            _metrics.RecordStartDelay(queue, job, DateTime.UtcNow - (job.IsAwaitingBatch ? claimedAt : job.ScheduledAt));

            // null: the outcome couldn't be saved, the job runs again once its lease expires
            string? outcome = null;

            try
            {
                outcome = await RunJobAsync(job, periodicJob, claimedAt, jobsToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (XactJobCancelException.Find(ex) != null)
                {
                    _logger.LogWarning(ex, "{Queue}: Job cancelled itself, it won't be retried: {TypeName}.{MethodName} ({Id})", GetQueueDisplayName(), job.TypeName, job.MethodName, job.Id);
                }
                else
                {
                    _logger.LogError(ex, "{Queue}: Job failed: {TypeName}.{MethodName} ({Id})", GetQueueDisplayName(), job.TypeName, job.MethodName, job.Id);
                }

                var recordedStatus = await RecordFailureAsync(job, periodicJob, claimedAt, ex)
                    .ConfigureAwait(false);

                outcome = recordedStatus switch
                {
                    XactJobStatus.Failed => XactJobsMetrics.Retried,
                    XactJobStatus.Cancelled => XactJobsMetrics.Failed,
                    _ => null,
                };

                _inFlightJobs.TryRemove(GetKey(job), out _);

                // finished: it won't be retried
                if (recordedStatus is XactJobStatus.Cancelled)
                {
                    await StartContinuationsIfBatchFinishedAsync(job)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                if (outcome != null)
                {
                    _metrics.RecordAttempt(queue, job, outcome, Stopwatch.GetElapsedTime(startedAt));
                }

                Interlocked.Decrement(ref _runningJobCount);

                _runSlots?.Release();

                _jobFinished.Writer.TryWrite(true);
            }
        }

        /// <summary>
        /// Waits for a slot to run a claimed job of a batch, and counts it as running. False if the worker is stopping: the job
        /// doesn't start. The count goes up before the check, so a shutdown that sees no running jobs can't miss one that starts.
        /// </summary>
        private async Task<bool> TakeRunSlotAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _runSlots!.WaitAsync(stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            Interlocked.Increment(ref _runningJobCount);

            if (!stoppingToken.IsCancellationRequested) return true;

            Interlocked.Decrement(ref _runningJobCount);

            _runSlots.Release();

            _jobFinished.Writer.TryWrite(true);

            return false;
        }

        private static (string Queue, DateTime ScheduledAt, long Id) GetKey(XactJob job)
        {
            return (job.Queue, job.ScheduledAt, job.Id);
        }

        /// <summary>
        /// Records a failed attempt in its own scope, after the job's scope has been rolled back and disposed.
        /// Never throws: if the record can't be saved, the lease expires and the job runs again.
        /// The stopping token isn't used, so that a job failing during shutdown still gets its failure recorded.
        /// <para>
        /// The retry is inserted before the failed attempt is deleted, in one transaction: so the job's batch can't look finished
        /// to a reader that doesn't use snapshots (SQL Server without snapshot isolation).
        /// </para>
        /// </summary>
        /// <returns>The status recorded in the history: Failed if the job is retried, Cancelled if it's finished. Null if it
        /// couldn't be recorded.</returns>
        private async Task<XactJobStatus?> RecordFailureAsync(XactJob job, XactJobPeriodic? periodicJob, DateTime claimedAt, Exception jobException)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();

                var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ClearLeaseTimeoutInSeconds));

                // a retrying execution strategy, if the app enabled one, only allows explicit transactions inside of it
                var status = await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
                {
                    dbContext.ChangeTracker.Clear();

                    await using var transaction = await dbContext.Database.BeginTransactionAsync(token)
                        .ConfigureAwait(false);

                    dbContext.Attach(job);

                    var activePeriodicJob = await GetIfStillActiveAsync(dbContext, periodicJob, token)
                        .ConfigureAwait(false);

                    var recordedStatus = RecordFailedAttempt(dbContext, job, activePeriodicJob, claimedAt, jobException);

                    await dbContext.SaveChangesAsync(token)
                        .ConfigureAwait(false);

                    dbContext.Set<XactJob>().Remove(job);

                    await dbContext.SaveChangesAsync(token)
                        .ConfigureAwait(false);

                    await transaction.CommitAsync(token)
                        .ConfigureAwait(false);

                    return recordedStatus;
                }, cts.Token).ConfigureAwait(false);

                return status;
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

            return null;
        }

        /// <summary>
        /// Wakes up the queues of the batch's continuations, if the job was the last one of its batch.
        /// Best effort: the workers also check for continuations on their own, on every polling interval.
        /// </summary>
        private async Task StartContinuationsIfBatchFinishedAsync(XactJob job)
        {
            if (job.BatchId == null) return;

            var batchId = job.BatchId.Value;

            try
            {
                using var scope = _scopeFactory.CreateScope();

                var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ClearLeaseTimeoutInSeconds));

                var jobs = dbContext.Set<XactJob>();

                // a parameter for each column: with one parameter for both, Oracle compared RAWTOHEX(BATCH_ID) with it in the
                // subquery, which can't use the index, and read the whole job table (QueryPlanScenarios.BatchQueries_UseTheBatchIndexes)
                var awaitedBatchId = batchId;

                var queues = await jobs
                    .Where(c => c.AwaitsBatchId == awaitedBatchId
                        && c.ScheduledAt == XactJobBase.AwaitingBatchScheduledAt
                        && !jobs.Any(j => j.BatchId == batchId))
                    .Select(c => c.Queue)
                    .Distinct()
                    .ToListAsync(cts.Token)
                    .ConfigureAwait(false);

                foreach (var queue in queues)
                {
                    _quickPollChannels.TryNotifyContinuationsReady(queue);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Queue}: Failed checking for the continuations of batch {BatchId}, they start on the next regular check", GetQueueDisplayName(), batchId);
            }
        }

        private Task ExtendLeaseAsync(CancellationToken token)
        {
            return ExecuteLeaseSqlForInFlightJobsAsync((dialect, jobs) => dialect.GetExtendLeaseSql(_leaser, jobs, _options.LeaseDurationInSeconds), token);
        }

        /// <returns>The outcome (see <see cref="XactJobsMetrics"/>): completed, skipped, or interrupted by the shutdown. A failure throws.</returns>
        public async Task<string> RunJobAsync(XactJob job, XactJobPeriodic? periodicJob, DateTime claimedAt, CancellationToken stoppingToken)
        {
            var isFinished = false;
            var outcome = XactJobsMetrics.Interrupted;

            using var scope = _scopeFactory.CreateScope();

            var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

            RequireTransactionForSaveChanges(dbContext);

            try
            {
                dbContext.Attach(job);

                var isSkipped = job.PeriodicJobId != null && (periodicJob == null || !periodicJob.IsActive || !periodicJob.IsCompatibleWith(job));

                string? skipReason = null;

                if (!isSkipped && job.IsAwaitingBatch)
                {
                    skipReason = await GetUnmetContinuationConditionAsync(dbContext, job, stoppingToken)
                        .ConfigureAwait(false);

                    isSkipped = skipReason != null;
                }

                if (isSkipped)
                {
                    // periodic job is inactive or deleted, or the continuation's condition isn't met
                    RecordSkipped(dbContext, job, periodicJob, claimedAt, skipReason);
                }
                else
                {
                    if (periodicJob != null)
                    {
                        // this is so we can detect if the job is deleted inside the job
                        dbContext.Attach(periodicJob);
                    }

                    var context = new XactJobContext(job.GetOriginallyScheduledAt(claimedAt));

                    // only while the job runs: the worker's own save, below, isn't checked
                    var saveCheck = _saveChecker.Subscribe(dbContext, job, _logger, GetQueueDisplayName());

                    try
                    {
                        await XactJobCompiler.CompileAndRunJobAsync(scope, job, context, stoppingToken)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        XactJobSaveChecker.Unsubscribe(dbContext, saveCheck);
                    }

                    // detached means the job deleted its own periodic job and saved that, so there's no next run to schedule
                    var isPeriodicJobDeleted = periodicJob != null && dbContext.Entry(periodicJob).State == EntityState.Detached;

                    var activePeriodicJob = isPeriodicJobDeleted
                        ? null
                        : await GetIfStillActiveAsync(dbContext, periodicJob, stoppingToken).ConfigureAwait(false);

                    RecordSuccess(dbContext, job, activePeriodicJob, claimedAt);
                }

                dbContext.Set<XactJob>().Remove(job);

                await dbContext.SaveChangesAsync(stoppingToken)
                    .ConfigureAwait(false);

                if (dbContext.Database.CurrentTransaction != null)
                {
                    await dbContext.Database.CurrentTransaction.CommitAsync(stoppingToken)
                        .ConfigureAwait(false);
                }

                _inFlightJobs.TryRemove(GetKey(job), out _);

                isFinished = true;
                outcome = isSkipped ? XactJobsMetrics.Skipped : XactJobsMetrics.Completed;

                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    _logger.LogDebug("{Queue}: Job {Outcome} - {TypeName}.{MethodName} ({Id})", GetQueueDisplayName(), isSkipped ? "skipped" : "completed", job.TypeName, job.MethodName, job.Id);
                }
            }
            catch (Exception ex)
            {
                // not only an OperationCanceledException: a cancelled command can throw the provider's own exception (SqlClient's SqlException).
                // Read before the rollback, so that a failure isn't taken for the shutdown if the shutdown only starts meanwhile
                var isCancelledByShutdown = stoppingToken.IsCancellationRequested && XactJobCancelException.Find(ex) == null;

                if (dbContext.Database.CurrentTransaction != null)
                {
                    // try to roll back always, even if stopping
                    await dbContext.Database.CurrentTransaction.RollbackAsync(CancellationToken.None)
                        .ConfigureAwait(false);
                }

                if (!isCancelledByShutdown)
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

            if (isFinished)
            {
                await StartContinuationsIfBatchFinishedAsync(job)
                    .ConfigureAwait(false);
            }

            return outcome;
        }

        /// <summary>
        /// A job of the batch failed if it ran out of retries or was cancelled (both are Cancelled in the history),
        /// or was a continuation that was skipped. The attempts that were retried are Failed, and don't count.
        /// </summary>
        /// <returns>Why the continuation is skipped, or null if it runs.</returns>
        private static async Task<string?> GetUnmetContinuationConditionAsync(TDbContext dbContext, XactJob job, CancellationToken cancellationToken)
        {
            var batchId = job.AwaitsBatchId;

            var failedCount = await dbContext.Set<XactJobHistory>()
                .CountAsync(h => h.BatchId == batchId && (h.Status == XactJobStatus.Cancelled || h.Status == XactJobStatus.Skipped), cancellationToken)
                .ConfigureAwait(false);

            var condition = job.ContinuationCondition ?? XactJobContinuation.OnSuccess;

            var isMet = condition switch
            {
                XactJobContinuation.OnFailure => failedCount > 0,
                XactJobContinuation.OnFinish => true,
                _ => failedCount == 0,
            };

            if (isMet) return null;

            return failedCount == 0
                ? $"Skipped: the batch {batchId} had no failed jobs, and the continuation runs on failure"
                : $"Skipped: the batch {batchId} had {failedCount} failed job(s), and the continuation runs on success";
        }


        /// <summary>
        /// The job row, the history row and the writes of the job must commit together, or not at all.
        /// EF doesn't always start a transaction for a single batch of statements and leaves it to the provider, and with
        /// MySql.EntityFrameworkCore the statements before a failed one stay committed.
        /// </summary>
        private static void RequireTransactionForSaveChanges(TDbContext dbContext)
        {
            dbContext.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;
        }

        /// <summary>
        /// The periodic job was loaded when the run was claimed, and it may have been paused or deleted while the job was running.
        /// Returns null if it's not active anymore, so that no next run is scheduled for it.
        /// </summary>
        private static async Task<XactJobPeriodic?> GetIfStillActiveAsync(TDbContext dbContext, XactJobPeriodic? periodicJob, CancellationToken cancellationToken)
        {
            if (periodicJob == null || !periodicJob.IsActive) return null;

            var state = dbContext.Entry(periodicJob).State;

            // changed by the job itself, and saved together with the run
            if (state == EntityState.Modified) return periodicJob;
            if (state == EntityState.Deleted) return null;

            // a list, because the Oracle provider can't translate IsActive (it has a value converter) in other ways
            var isActive = await dbContext.Set<XactJobPeriodic>()
                .Where(p => p.Id == periodicJob.Id)
                .Select(p => p.IsActive)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return isActive.Count == 1 && isActive[0] ? periodicJob : null;
        }

        private void RecordSuccess(TDbContext dbContext, XactJob job, XactJobPeriodic? periodicJob, DateTime claimedAt)
        {
            RecordProcessingAttempt(dbContext, job, periodicJob, claimedAt, ProcessingResult.Completed, null, null);
        }

        private void RecordSkipped(TDbContext dbContext, XactJob job, XactJobPeriodic? periodicJob, DateTime claimedAt, string? reason)
        {
            RecordProcessingAttempt(dbContext, job, periodicJob, claimedAt, ProcessingResult.Skipped, null, reason);
        }

        private XactJobStatus RecordFailedAttempt(TDbContext dbContext, XactJob job, XactJobPeriodic? periodicJob, DateTime claimedAt, Exception ex)
        {
            return RecordProcessingAttempt(dbContext, job, periodicJob, claimedAt, ProcessingResult.Failed, ex, null);
        }

        /// <summary>
        /// Adds the history row, and the retry or the next periodic run. The caller removes the job's row.
        /// </summary>
        private XactJobStatus RecordProcessingAttempt(TDbContext dbContext,
                                                      XactJob job,
                                                      XactJobPeriodic? periodicJob,
                                                      DateTime claimedAt,
                                                      ProcessingResult processingResult,
                                                      Exception? ex,
                                                      string? message)
        {
            var status = processingResult switch
            {
                ProcessingResult.Completed => XactJobStatus.Completed,
                ProcessingResult.Failed => XactJobStatus.Failed,
                ProcessingResult.Skipped => XactJobStatus.Skipped,
                _ => throw new ArgumentOutOfRangeException(nameof(processingResult)),
            };

            var errorCount = job.ErrorCount;
            var cancel = XactJobCancelException.Find(ex);

            if (status == XactJobStatus.Failed)
            {
                errorCount++;

                // the job said it can never succeed: no retries
                var nextRunTimeUtc = cancel == null ? _options.RetryStrategy.GetRetryTimeUtc(job, errorCount) : null;

                if (nextRunTimeUtc.HasValue)
                {
                    dbContext.Reschedule(job, nextRunTimeUtc.Value, errorCount, job.GetOriginallyScheduledAt(claimedAt));
                }
                else
                {
                    status = XactJobStatus.Cancelled;
                }
            }

            var history = CreateHistory(job, status, errorCount, ex, cancel, message, claimedAt);

            dbContext.Set<XactJobHistory>().Add(history);

            if (periodicJob != null 
                && periodicJob.IsActive // a paused job has no pending run, the next one is scheduled when it's resumed
                && status != XactJobStatus.Failed // only failed jobs are not re-scheduled to prevent overlap (they are re-scheduled above)
                && periodicJob.IsCompatibleWith(job) 
                && dbContext.Entry(periodicJob).State != EntityState.Deleted)
            {
                dbContext.ScheduleNextRun(periodicJob);
            }

            return status;
        }

        /// <summary>
        /// For a cancelled job, the message is the one of the cancel exception, not of its innermost exception (the cause), which is in the details.
        /// A continuation that waited for its batch gets the time it was claimed as its scheduled time.
        /// </summary>
        private static XactJobHistory CreateHistory(XactJob job,
                                                    XactJobStatus status,
                                                    int errorCount,
                                                    Exception? ex,
                                                    XactJobCancelException? cancel,
                                                    string? message,
                                                    DateTime claimedAt)
        {
            var scheduledAt = job.IsAwaitingBatch ? claimedAt : job.ScheduledAt;

            if (cancel != null)
            {
                return XactJobHistory.CreateFromJob(job, DateTime.UtcNow, status, errorCount, cancel.Message, cancel.ToString(), scheduledAt);
            }

            var innerMostEx = ex;

            while (innerMostEx?.InnerException != null)
            {
                innerMostEx = innerMostEx.InnerException;
            }

            return XactJobHistory.CreateFromJob(job, DateTime.UtcNow, status, errorCount, innerMostEx?.Message ?? message, ex?.StackTrace, scheduledAt);
        }

        private static DateTime TruncateToMicroseconds(DateTime value)
        {
            return new DateTime(value.Ticks - value.Ticks % 10, value.Kind);
        }

        string IXactJobsWorkerState.QueueDisplayName => GetQueueDisplayName();

        int IXactJobsWorkerState.RunningJobs => Volatile.Read(ref _runningJobCount);

        int IXactJobsWorkerState.HeldJobs => _inFlightJobs.Count;

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
