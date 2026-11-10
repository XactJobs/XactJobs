using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Services;

public class XactJobsUIService<TDbContext> : IXactJobsUIService
    where TDbContext : DbContext
{
    /// <summary>
    /// The error message of a job cancelled from the dashboard. It has no stack trace, unlike a job that ran out of retries.
    /// </summary>
    public const string CancelledMessage = "Cancelled from the dashboard";

    public const int MaxPageSize = 500;

    /// <summary>
    /// The most intervals of the chart: each is a query.
    /// </summary>
    public const int MaxChartIntervals = 200;

    private readonly TDbContext db;
    private readonly QuickPollChannels? quickPollChannels;
    private readonly XactJobsOptions<TDbContext>? jobsOptions;

    /// <param name="quickPollChannels">Registered by AddXactJobs. Without it (no workers in this process),
    /// a job made due by the dashboard starts on the next poll.</param>
    /// <param name="jobsOptions">Registered by AddXactJobs, for the configured queues.</param>
    public XactJobsUIService(TDbContext dbContext, QuickPollChannels? quickPollChannels = null, XactJobsOptions<TDbContext>? jobsOptions = null)
    {
        db = dbContext;
        this.quickPollChannels = quickPollChannels;
        this.jobsOptions = jobsOptions;
    }

    public async Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var last24h = now.AddHours(-24);

        var processingJobs = await db.Set<XactJob>()
            .CountAsync(j => j.LeasedUntil != null && j.LeasedUntil >= now, ct);

        var dueJobs = await db.Set<XactJob>()
            .CountAsync(j => (j.LeasedUntil == null || j.LeasedUntil < now) && j.ScheduledAt <= now, ct);

        var awaiting = XactJobBase.AwaitingBatchScheduledAt;

        var scheduledJobs = await db.Set<XactJob>()
            .CountAsync(j => (j.LeasedUntil == null || j.LeasedUntil < now) && j.ScheduledAt > now && j.ScheduledAt != awaiting, ct);

        var awaitingContinuations = await db.Set<XactJob>()
            .CountAsync(j => (j.LeasedUntil == null || j.LeasedUntil < now) && j.ScheduledAt == awaiting, ct);

        var historyLast24h = await db.Set<XactJobHistory>()
            .Where(h => h.ProcessedAt >= last24h)
            .GroupBy(h => h.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var cancelledFromDashboard = await ApplyCategory(db.Set<XactJobHistory>(), HistoryCategory.Cancelled)
            .CountAsync(h => h.ProcessedAt >= last24h, ct);

        int CountOf(XactJobStatus status) => historyLast24h.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

        var succeeded = CountOf(XactJobStatus.Completed);
        var failed = CountOf(XactJobStatus.Cancelled) - cancelledFromDashboard;
        var finished = succeeded + failed;

        // a list, because the Oracle provider can't translate IsActive (it has a value converter) in other ways
        var periodicActive = await db.Set<XactJobPeriodic>()
            .Select(p => p.IsActive)
            .ToListAsync(ct);

        return new DashboardStatsDto
        {
            DueJobs = dueJobs,
            ScheduledJobs = scheduledJobs,
            AwaitingContinuations = awaitingContinuations,
            ProcessingJobs = processingJobs,
            SucceededLast24h = succeeded,
            FailedLast24h = failed,
            RetriedLast24h = CountOf(XactJobStatus.Failed),
            PeriodicJobsActive = periodicActive.Count(x => x),
            PeriodicJobsInactive = periodicActive.Count(x => !x),
            SuccessRateLast24h = finished > 0 ? (double)succeeded / finished * 100 : 100
        };
    }

    /// <summary>
    /// One grouped count per interval, so that only the counts leave the database (the history can be large).
    /// A portable query can't group by interval, the date functions differ between the databases.
    /// </summary>
    public async Task<IReadOnlyList<JobCountsOverTimeDto>> GetJobCountsOverTimeAsync(
        DateTime from, DateTime to, TimeSpan interval, CancellationToken ct = default)
    {
        if (interval < TimeSpan.FromMinutes(1)) interval = TimeSpan.FromMinutes(1);

        if ((to - from).Ticks / interval.Ticks > MaxChartIntervals)
        {
            interval = TimeSpan.FromTicks((to - from).Ticks / MaxChartIntervals + 1);
        }

        // usually few, so they are counted here
        var cancelledFromDashboard = await ApplyCategory(db.Set<XactJobHistory>(), HistoryCategory.Cancelled)
            .Where(h => h.ProcessedAt >= from && h.ProcessedAt < to)
            .Select(h => h.ProcessedAt)
            .ToListAsync(ct);

        var results = new List<JobCountsOverTimeDto>();

        for (var current = from; current < to; current += interval)
        {
            var start = current;
            var end = current + interval < to ? current + interval : to;

            var counts = await db.Set<XactJobHistory>()
                .Where(h => h.ProcessedAt >= start && h.ProcessedAt < end)
                .GroupBy(h => h.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            int CountOf(XactJobStatus status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

            var cancelled = cancelledFromDashboard.Count(t => t >= start && t < end);

            results.Add(new JobCountsOverTimeDto
            {
                Timestamp = DateTime.SpecifyKind(start, DateTimeKind.Utc),
                Succeeded = CountOf(XactJobStatus.Completed),
                Failed = CountOf(XactJobStatus.Cancelled) - cancelled,
                Retried = CountOf(XactJobStatus.Failed),
                Cancelled = cancelled,
                Skipped = CountOf(XactJobStatus.Skipped)
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<string>> GetQueuesAsync(CancellationToken ct = default)
    {
        var queues = new SortedSet<string>(StringComparer.Ordinal) { QueueNames.Default };

        if (jobsOptions != null)
        {
            queues.UnionWith(jobsOptions.IsolatedQueues.Keys);
        }

        queues.UnionWith(await db.Set<XactJob>().Select(j => j.Queue).Distinct().ToListAsync(ct));
        queues.UnionWith(await db.Set<XactJobPeriodic>().Select(p => p.Queue).Distinct().ToListAsync(ct));

        // the default queue first
        return [QueueNames.Default, .. queues.Where(q => q != QueueNames.Default)];
    }

    public async Task<PagedResultDto<JobDto>> GetScheduledJobsAsync(
        int page, int pageSize, string? queue = null, CancellationToken ct = default)
    {
        (page, pageSize) = Normalize(page, pageSize);

        var query = db.Set<XactJob>().AsNoTracking();

        if (!string.IsNullOrEmpty(queue))
        {
            query = query.Where(j => j.Queue == queue);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(j => j.ScheduledAt)
            .ThenBy(j => j.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToJobDto)
            .ToListAsync(ct);

        return new PagedResultDto<JobDto>
        {
            Items = await WithBatchJobsLeftAsync(items, ct),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<JobDto?> GetJobAsync(JobKey key, CancellationToken ct = default)
    {
        var job = await WhereKey(db.Set<XactJob>().AsNoTracking(), key)
            .Select(ToJobDto)
            .FirstOrDefaultAsync(ct);

        return job == null ? null : (await WithBatchJobsLeftAsync([job], ct))[0];
    }

    public async Task<ActionResultDto> CancelJobAsync(JobKey key, string? cancelledBy, CancellationToken ct = default)
    {
        var id = key.Id;
        var job = await FindJobAsync(key, ct);

        if (job == null) return JobNotFound(id);

        if (job.PeriodicJobId != null)
        {
            return ActionResultDto.Conflict($"Job #{id} is a run of the periodic job '{job.PeriodicJobId}'. Pause the periodic job instead.");
        }

        var message = string.IsNullOrEmpty(cancelledBy) ? CancelledMessage : $"{CancelledMessage} by {cancelledBy}";

        var conflict = await ReplacePendingJobAsync(job, now =>
        {
            // a continuation that waits for its batch has a reserved scheduled time, which doesn't belong in the history
            db.Set<XactJobHistory>().Add(new XactJobHistory(job.Id,
                                                            now,
                                                            XactJobStatus.Cancelled,
                                                            job.IsAwaitingBatch ? now : job.ScheduledAt,
                                                            job.TypeName,
                                                            job.MethodName,
                                                            job.MethodArgs,
                                                            job.Queue,
                                                            job.PeriodicJobId,
                                                            job.CronExpression,
                                                            job.PeriodicJobVersion,
                                                            job.ErrorCount,
                                                            message,
                                                            errorStackTrace: null)
            {
                BatchId = job.BatchId,
                AwaitsBatchId = job.AwaitsBatchId,
                ContinuationCondition = job.ContinuationCondition,
                OriginallyScheduledAt = job.OriginallyScheduledAt,
                TraceParent = job.TraceParent
            });
        }, ct);

        if (conflict != null) return conflict;

        if (job.BatchId != null)
        {
            await StartContinuationsIfBatchFinishedAsync(job.BatchId.Value, ct);
        }

        return ActionResultDto.Done($"Job #{id} cancelled.");
    }

    public async Task<ActionResultDto> RunJobNowAsync(JobKey key, CancellationToken ct = default)
    {
        var id = key.Id;
        var job = await FindJobAsync(key, ct);

        if (job == null) return JobNotFound(id);

        return await RunNowAsync(job, $"Job #{id}", ct);
    }

    public async Task<PagedResultDto<JobHistoryDto>> GetHistoryAsync(
        HistoryCategory category, int page, int pageSize, string? queue = null, CancellationToken ct = default)
    {
        (page, pageSize) = Normalize(page, pageSize);

        var query = ApplyCategory(db.Set<XactJobHistory>().AsNoTracking(), category);

        if (!string.IsNullOrEmpty(queue))
        {
            query = query.Where(h => h.Queue == queue);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(h => h.ProcessedAt)
            .ThenByDescending(h => h.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToJobHistoryDto)
            .ToListAsync(ct);

        return new PagedResultDto<JobHistoryDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<JobHistoryDto?> GetJobHistoryByIdAsync(long id, CancellationToken ct = default)
    {
        return await db.Set<XactJobHistory>()
            .AsNoTracking()
            .Where(h => h.Id == id)
            .OrderByDescending(h => h.ProcessedAt) // the last attempt: the retries of a job have the same id
            .Select(ToJobHistoryDto)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ActionResultDto> RetryJobAsync(long historyId, CancellationToken ct = default)
    {
        // the last attempt of the job: its retries have the same id
        var history = await db.Set<XactJobHistory>()
            .AsNoTracking()
            .Where(h => h.Id == historyId)
            .OrderByDescending(h => h.ProcessedAt)
            .FirstOrDefaultAsync(ct);

        if (history == null) return ActionResultDto.NotFound($"Job #{historyId} not found in the history.");

        var isSkippedContinuation = history.Status == XactJobStatus.Skipped && history.AwaitsBatchId != null;

        if (history.Status != XactJobStatus.Cancelled && !isSkippedContinuation)
        {
            return ActionResultDto.Conflict($"Job #{historyId} can't be retried: only failed and cancelled jobs can, and continuations that were skipped.");
        }

        // as a one-off job, a run of a periodic job would run without its state, which only the periodic job's runs get
        if (history.PreviousState != null || history.State != null)
        {
            return ActionResultDto.Conflict($"Job #{historyId} can't be retried: it's a run of the periodic job '{history.PeriodicJobId}' with a state, and a retry would run without it.");
        }

        // a one-off job: as a run of the periodic job, it would schedule a second chain of runs.
        // Outside of any batch: the continuations of its batch, if any, already started or were skipped.
        // it keeps the time the job was originally scheduled for, as a retry by a worker does
        var newJob = new XactJob(0,
                                 DateTime.UtcNow,
                                 history.TypeName,
                                 history.MethodName,
                                 history.MethodArgs,
                                 history.Queue)
        {
            OriginallyScheduledAt = history.OriginallyScheduledAt ?? history.ScheduledAt,
            TraceParent = history.TraceParent
        };

        db.Set<XactJob>().Add(newJob);

        await db.SaveChangesAsync(ct);

        NotifyQueue(newJob.Queue);

        return ActionResultDto.Done($"Job #{historyId} enqueued again as job #{newJob.Id}.");
    }

    public async Task<PagedResultDto<PeriodicJobDto>> GetPeriodicJobsAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Normalize(page, pageSize);

        var query = db.Set<XactJobPeriodic>().AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var periodicJobs = await query
            .OrderBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var ids = periodicJobs.Select(p => p.Id).ToList();

        var runs = ids.Count == 0
            ? []
            : await db.Set<XactJob>()
                .AsNoTracking()
                .Where(j => j.PeriodicJobId != null && ids.Contains(j.PeriodicJobId))
                .Select(j => new { j.PeriodicJobId, Version = j.PeriodicJobVersion ?? 1, j.ScheduledAt, j.LeasedUntil })
                .ToListAsync(ct);

        var now = DateTime.UtcNow;

        var items = periodicJobs
            .Select(p =>
            {
                var currentRuns = runs.Where(r => r.PeriodicJobId == p.Id && r.Version == p.Version).ToList();

                return ToDto(p) with
                {
                    NextRunAt = currentRuns.Count > 0 ? currentRuns.Min(r => r.ScheduledAt) : null,
                    IsRunning = currentRuns.Any(r => r.LeasedUntil >= now)
                };
            })
            .ToList();

        return new PagedResultDto<PeriodicJobDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<PeriodicJobDto?> GetPeriodicJobByIdAsync(string id, CancellationToken ct = default)
    {
        var periodicJob = await db.Set<XactJobPeriodic>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        return periodicJob == null ? null : ToDto(periodicJob);
    }

    public async Task<ActionResultDto> SetPeriodicJobActiveAsync(string id, bool isActive, CancellationToken ct = default)
    {
        if (!await db.JobSetPeriodicActiveAsync(id, isActive, ct))
        {
            return PeriodicJobNotFound(id);
        }

        await db.SaveChangesAsync(ct);

        return ActionResultDto.Done(isActive ? $"Periodic job '{id}' resumed." : $"Periodic job '{id}' paused.");
    }

    public async Task<ActionResultDto> TriggerPeriodicJobAsync(string id, CancellationToken ct = default)
    {
        var periodicJob = await db.Set<XactJobPeriodic>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (periodicJob == null) return PeriodicJobNotFound(id);

        if (!periodicJob.IsActive)
        {
            return ActionResultDto.Conflict($"Periodic job '{id}' is paused. Resume it first.");
        }

        var version = periodicJob.Version;

        var runs = await db.Set<XactJob>()
            .AsNoTracking()
            .Where(j => j.PeriodicJobId == id && (j.PeriodicJobVersion ?? 1) == version)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;

        if (runs.Any(r => IsLeased(r, now)))
        {
            return ActionResultDto.Conflict($"Periodic job '{id}' is already running.");
        }

        var pending = runs.OrderBy(r => r.ScheduledAt).FirstOrDefault();

        if (pending != null)
        {
            return await RunNowAsync(pending, $"Periodic job '{id}'", ct);
        }

        // the chain of runs got lost: this run starts it again
        var job = new XactJob(0,
                              now,
                              periodicJob.TypeName,
                              periodicJob.MethodName,
                              periodicJob.MethodArgs,
                              periodicJob.Queue,
                              periodicJob.Id,
                              periodicJob.CronExpression,
                              periodicJob.Version);

        db.Set<XactJob>().Add(job);

        await db.SaveChangesAsync(ct);

        NotifyQueue(job.Queue);

        return ActionResultDto.Done($"Periodic job '{id}' triggered as job #{job.Id}.");
    }

    public async Task<ActionResultDto> DeletePeriodicJobAsync(string id, CancellationToken ct = default)
    {
        if (!await db.JobDeletePeriodicAsync(id, ct))
        {
            return PeriodicJobNotFound(id);
        }

        await db.SaveChangesAsync(ct);

        return ActionResultDto.Done($"Periodic job '{id}' deleted.");
    }

    private async Task<ActionResultDto> RunNowAsync(XactJob job, string subject, CancellationToken ct)
    {
        if (IsLeased(job, DateTime.UtcNow))
        {
            return ActionResultDto.Conflict($"{subject} is running.");
        }

        if (job.IsAwaitingBatch)
        {
            return ActionResultDto.Conflict($"{subject} is a continuation of the batch {job.AwaitsBatchId}: it starts when the batch is finished.");
        }

        if (job.ScheduledAt <= DateTime.UtcNow)
        {
            NotifyQueue(job.Queue);

            return ActionResultDto.Done($"{subject} is already due, it starts as soon as a worker is free.");
        }

        if (!await MoveToNowAsync(job, ct))
        {
            return ActionResultDto.Conflict($"{subject} started running or finished meanwhile.");
        }

        NotifyQueue(job.Queue);

        return ActionResultDto.Done($"{subject} runs now.");
    }

    /// <summary>
    /// Makes a pending job due now, if no worker has leased it: the same row, with the scheduled time now. It keeps the time it was
    /// scheduled for, e.g. the cron slot of a periodic job. The scheduled time is part of the primary key, so it's an UPDATE by the
    /// old key, in one statement.
    /// </summary>
    /// <returns>False if a worker leased the row, or it's gone.</returns>
    private async Task<bool> MoveToNowAsync(XactJob job, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var originallyScheduledAt = job.OriginallyScheduledAt ?? job.ScheduledAt;

        var updated = await WhereKey(db.Set<XactJob>(), JobKey.Of(job))
            .Where(j => j.LeasedUntil == null || j.LeasedUntil < now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.ScheduledAt, now)
                .SetProperty(j => j.OriginallyScheduledAt, originallyScheduledAt), ct);

        return updated > 0;
    }

    /// <summary>
    /// Deletes the job row if no worker has leased it, and adds what replaces it in the history, in one transaction.
    /// </summary>
    /// <returns>A conflict if a worker leased the row, or it's gone.</returns>
    private async Task<ActionResultDto?> ReplacePendingJobAsync(XactJob job, Action<DateTime> addReplacement, CancellationToken ct)
    {
        if (IsLeased(job, DateTime.UtcNow))
        {
            return ActionResultDto.Conflict($"Job #{job.Id} is running.");
        }

        var strategy = db.Database.CreateExecutionStrategy();

        var replaced = await strategy.ExecuteAsync(async token =>
        {
            db.ChangeTracker.Clear();

            await using var transaction = await db.Database.BeginTransactionAsync(token);

            var now = DateTime.UtcNow;

            addReplacement(now);

            await db.SaveChangesAsync(token);

            // the whole primary key, so the delete can use it
            var deleted = await db.Set<XactJob>()
                .Where(j => j.Queue == job.Queue
                    && j.ScheduledAt == job.ScheduledAt
                    && j.Id == job.Id
                    && (j.LeasedUntil == null || j.LeasedUntil < now))
                .ExecuteDeleteAsync(token);

            // rolled back when the transaction is disposed
            if (deleted == 0) return false;

            await transaction.CommitAsync(token);

            return true;
        }, ct);

        return replaced ? null : ActionResultDto.Conflict($"Job #{job.Id} started running or finished meanwhile.");
    }

    public async Task<PagedResultDto<BatchDto>> GetBatchesAsync(BatchState state, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Normalize(page, pageSize);

        return state == BatchState.Active
            ? await GetActiveBatchesAsync(page, pageSize, ct)
            : await GetFinishedBatchesAsync(page, pageSize, ct);
    }

    public async Task<BatchDetailsDto?> GetBatchAsync(Guid id, CancellationToken ct = default)
    {
        var jobs = await db.Set<XactJob>()
            .AsNoTracking()
            .Where(j => j.BatchId == id)
            .OrderBy(j => j.ScheduledAt)
            .ThenBy(j => j.Id)
            .Take(MaxPageSize)
            .Select(ToJobDto)
            .ToListAsync(ct);

        var history = await db.Set<XactJobHistory>()
            .AsNoTracking()
            .Where(h => h.BatchId == id)
            .OrderByDescending(h => h.ProcessedAt)
            .ThenByDescending(h => h.Id)
            .Take(MaxPageSize)
            .Select(ToJobHistoryDto)
            .ToListAsync(ct);

        var pendingContinuations = await db.Set<XactJob>()
            .AsNoTracking()
            .Where(j => j.AwaitsBatchId == id)
            .OrderBy(j => j.Id)
            .Take(MaxPageSize)
            .Select(ToJobDto)
            .ToListAsync(ct);

        var finishedContinuations = await db.Set<XactJobHistory>()
            .AsNoTracking()
            .Where(h => h.AwaitsBatchId == id)
            .OrderByDescending(h => h.ProcessedAt)
            .ThenByDescending(h => h.Id)
            .Take(MaxPageSize)
            .Select(ToJobHistoryDto)
            .ToListAsync(ct);

        if (jobs.Count == 0 && history.Count == 0 && pendingContinuations.Count == 0 && finishedContinuations.Count == 0)
        {
            return null;
        }

        var batch = (await GetBatchSummariesAsync([id], ct)).GetValueOrDefault(id) ?? new BatchDto { Id = id };

        var continuesBatches = jobs.Select(j => j.AwaitsBatchId)
            .Concat(history.Select(h => h.AwaitsBatchId))
            .OfType<Guid>()
            .Distinct()
            .ToList();

        return new BatchDetailsDto
        {
            Batch = batch,
            Jobs = jobs,
            History = history,
            PendingContinuations = await WithBatchJobsLeftAsync(pendingContinuations, ct),
            FinishedContinuations = finishedContinuations,
            ContinuesBatches = continuesBatches
        };
    }

    /// <summary>
    /// Batches with jobs left, the earliest first: the ones whose jobs are all continuations that wait for another batch come last.
    /// </summary>
    private async Task<PagedResultDto<BatchDto>> GetActiveBatchesAsync(int page, int pageSize, CancellationToken ct)
    {
        var groups = db.Set<XactJob>()
            .Where(j => j.BatchId != null)
            .GroupBy(j => j.BatchId)
            .Select(g => new { Id = g.Key, FirstAt = g.Min(j => j.ScheduledAt) });

        var totalCount = await groups.CountAsync(ct);

        var ids = await groups
            .OrderBy(g => g.FirstAt)
            .ThenBy(g => g.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => g.Id!.Value)
            .ToListAsync(ct);

        var summaries = await GetBatchSummariesAsync(ids, ct);

        return new PagedResultDto<BatchDto>
        {
            Items = ids.Select(id => summaries[id]).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Batches without jobs left, the latest finished first.
    /// </summary>
    private async Task<PagedResultDto<BatchDto>> GetFinishedBatchesAsync(int page, int pageSize, CancellationToken ct)
    {
        var jobs = db.Set<XactJob>();

        var groups = db.Set<XactJobHistory>()
            .Where(h => h.BatchId != null && !jobs.Any(j => j.BatchId == h.BatchId))
            .GroupBy(h => h.BatchId)
            .Select(g => new { Id = g.Key, LastAt = g.Max(h => h.ProcessedAt) });

        var totalCount = await groups.CountAsync(ct);

        var ids = await groups
            .OrderByDescending(g => g.LastAt)
            .ThenBy(g => g.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => g.Id!.Value)
            .ToListAsync(ct);

        var summaries = await GetBatchSummariesAsync(ids, ct);

        return new PagedResultDto<BatchDto>
        {
            Items = ids.Select(id => summaries[id]).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// The counts of the given batches, a few grouped queries for all of them.
    /// </summary>
    private async Task<Dictionary<Guid, BatchDto>> GetBatchSummariesAsync(List<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];

        var now = DateTime.UtcNow;
        var awaiting = XactJobBase.AwaitingBatchScheduledAt;
        var batchIds = ids.Select(id => (Guid?)id).ToList();

        // "IS NOT NULL" with each Contains: the batch columns' indexes leave out the null rows (filtered), and SQL Server uses them
        // only if the query says so. EF Core 9 sends the list as a JSON parameter, which doesn't (EF Core 10 sends parameters)
        var pending = await db.Set<XactJob>()
            .Where(j => j.BatchId != null && batchIds.Contains(j.BatchId))
            .GroupBy(j => j.BatchId)
            .Select(g => new
            {
                Id = g.Key,
                Count = g.Count(),
                Running = g.Count(j => j.LeasedUntil != null && j.LeasedUntil >= now),
                NextRunAt = g.Min(j => j.ScheduledAt == awaiting ? (DateTime?)null : j.ScheduledAt),
                SampleId = g.Min(j => j.Id)
            })
            .ToListAsync(ct);

        var finished = await db.Set<XactJobHistory>()
            .Where(h => h.BatchId != null && batchIds.Contains(h.BatchId))
            .GroupBy(h => h.BatchId)
            .Select(g => new
            {
                Id = g.Key,
                Succeeded = g.Count(h => h.Status == XactJobStatus.Completed),
                Failed = g.Count(h => h.Status == XactJobStatus.Cancelled || h.Status == XactJobStatus.Skipped),
                LastProcessedAt = g.Max(h => h.ProcessedAt),
                SampleId = g.Min(h => h.Id)
            })
            .ToListAsync(ct);

        var continuations = await db.Set<XactJob>()
            .Where(j => j.ScheduledAt == awaiting && j.AwaitsBatchId != null && batchIds.Contains(j.AwaitsBatchId))
            .GroupBy(j => j.AwaitsBatchId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        // a job of each batch, to tell them apart: a pending one, or one from the history
        var pendingSampleIds = pending.Select(p => p.SampleId).ToList();
        var historySampleIds = finished.Where(f => pending.All(p => p.Id != f.Id)).Select(f => f.SampleId).ToList();

        // with the batch, so it uses the index of the batch column: on most databases there's no index on the id alone
        var samples = await db.Set<XactJob>()
            .Where(j => pendingSampleIds.Contains(j.Id) && j.BatchId != null && batchIds.Contains(j.BatchId))
            .Select(j => new { j.BatchId, j.TypeName, j.MethodName })
            .ToListAsync(ct);

        samples.AddRange(await db.Set<XactJobHistory>()
            .Where(h => historySampleIds.Contains(h.Id) && h.BatchId != null && batchIds.Contains(h.BatchId))
            .Select(h => new { h.BatchId, h.TypeName, h.MethodName })
            .ToListAsync(ct));

        return ids.ToDictionary(id => id, id =>
        {
            var p = pending.FirstOrDefault(x => x.Id == id);
            var f = finished.FirstOrDefault(x => x.Id == id);
            var sample = samples.FirstOrDefault(x => x.BatchId == id);

            return new BatchDto
            {
                Id = id,
                PendingJobs = p?.Count ?? 0,
                RunningJobs = p?.Running ?? 0,
                NextRunAt = p?.NextRunAt,
                SucceededJobs = f?.Succeeded ?? 0,
                FailedJobs = f?.Failed ?? 0,
                LastProcessedAt = f?.LastProcessedAt,
                AwaitingContinuations = continuations.FirstOrDefault(x => x.Id == id)?.Count ?? 0,
                TypeName = sample?.TypeName,
                MethodName = sample?.MethodName
            };
        });
    }

    /// <summary>
    /// Adds the number of jobs left in the batch that each waiting continuation waits for.
    /// </summary>
    private async Task<List<JobDto>> WithBatchJobsLeftAsync(List<JobDto> jobs, CancellationToken ct)
    {
        var awaitedIds = jobs.Where(j => j.IsAwaitingBatch).Select(j => j.AwaitsBatchId).Distinct().ToList();

        if (awaitedIds.Count == 0) return jobs;

        var counts = await db.Set<XactJob>()
            .Where(j => awaitedIds.Contains(j.BatchId))
            .GroupBy(j => j.BatchId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return jobs
            .Select(j => j.IsAwaitingBatch ? j with { BatchJobsLeft = counts.FirstOrDefault(c => c.Id == j.AwaitsBatchId)?.Count ?? 0 } : j)
            .ToList();
    }

    /// <summary>
    /// Wakes up the workers of the continuations of the batch, if it's finished. Best effort: the workers also check on their own.
    /// </summary>
    private async Task StartContinuationsIfBatchFinishedAsync(Guid batchId, CancellationToken ct)
    {
        if (quickPollChannels == null) return;

        var jobs = db.Set<XactJob>();
        var awaiting = XactJobBase.AwaitingBatchScheduledAt;

        // a parameter for each column: with one parameter for both, Oracle compared RAWTOHEX(BATCH_ID) with it in the subquery,
        // which can't use the index, and read the whole job table (see XactJobRunner.StartContinuationsIfBatchFinishedAsync)
        var awaitedBatchId = batchId;

        var queues = await jobs
            .Where(c => c.AwaitsBatchId == awaitedBatchId && c.ScheduledAt == awaiting && !jobs.Any(j => j.BatchId == batchId))
            .Select(c => c.Queue)
            .Distinct()
            .ToListAsync(ct);

        foreach (var queue in queues)
        {
            quickPollChannels.TryNotifyContinuationsReady(queue);
        }
    }

    private static readonly Expression<Func<XactJob, JobDto>> ToJobDto = j => new JobDto
    {
        Id = j.Id,
        ScheduledAt = j.ScheduledAt,
        TypeName = j.TypeName,
        MethodName = j.MethodName,
        Queue = j.Queue,
        ErrorCount = j.ErrorCount,
        LeasedUntil = j.LeasedUntil,
        Leaser = j.Leaser,
        PeriodicJobId = j.PeriodicJobId,
        BatchId = j.BatchId,
        AwaitsBatchId = j.AwaitsBatchId,
        ContinuationCondition = j.ContinuationCondition,
        OriginallyScheduledAt = j.OriginallyScheduledAt != j.ScheduledAt ? j.OriginallyScheduledAt : null
    };

    private static readonly Expression<Func<XactJobHistory, JobHistoryDto>> ToJobHistoryDto = h => new JobHistoryDto
    {
        Id = h.Id,
        ProcessedAt = h.ProcessedAt,
        ScheduledAt = h.ScheduledAt,
        Status = h.Status,
        TypeName = h.TypeName,
        MethodName = h.MethodName,
        Queue = h.Queue,
        ErrorCount = h.ErrorCount,
        ErrorMessage = h.ErrorMessage,
        ErrorStackTrace = h.ErrorStackTrace,
        PeriodicJobId = h.PeriodicJobId,
        BatchId = h.BatchId,
        AwaitsBatchId = h.AwaitsBatchId,
        ContinuationCondition = h.ContinuationCondition,
        OriginallyScheduledAt = h.OriginallyScheduledAt != h.ScheduledAt ? h.OriginallyScheduledAt : null,
        PreviousState = h.PreviousState,
        State = h.State
    };

    private Task<XactJob?> FindJobAsync(JobKey key, CancellationToken ct)
    {
        return WhereKey(db.Set<XactJob>().AsNoTracking(), key).FirstOrDefaultAsync(ct);
    }

    // the whole primary key: on PostgreSQL, SQL Server and Oracle there's no index on the id alone
    private static IQueryable<XactJob> WhereKey(IQueryable<XactJob> jobs, JobKey key)
    {
        return jobs.Where(j => j.Queue == key.Queue && j.ScheduledAt == key.ScheduledAt && j.Id == key.Id);
    }

    private void NotifyQueue(string queue)
    {
        quickPollChannels?.TryNotify(queue);
    }

    private static bool IsLeased(XactJob job, DateTime now)
    {
        return job.LeasedUntil != null && job.LeasedUntil >= now;
    }

    private static IQueryable<XactJobHistory> ApplyCategory(IQueryable<XactJobHistory> query, HistoryCategory category)
    {
        return category switch
        {
            HistoryCategory.Succeeded => query.Where(h => h.Status == XactJobStatus.Completed),
            HistoryCategory.Failed => query.Where(h => h.Status == XactJobStatus.Cancelled && h.ErrorStackTrace != null),
            HistoryCategory.Cancelled => query.Where(h => h.Status == XactJobStatus.Cancelled && h.ErrorStackTrace == null),
            HistoryCategory.Retried => query.Where(h => h.Status == XactJobStatus.Failed),
            HistoryCategory.Skipped => query.Where(h => h.Status == XactJobStatus.Skipped),
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };
    }

    private static (int Page, int PageSize) Normalize(int page, int pageSize)
    {
        return (Math.Max(page, 1), Math.Clamp(pageSize, 1, MaxPageSize));
    }

    private static PeriodicJobDto ToDto(XactJobPeriodic p)
    {
        return new PeriodicJobDto
        {
            Id = p.Id,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            CronExpression = p.CronExpression,
            TypeName = p.TypeName,
            MethodName = p.MethodName,
            Queue = p.Queue,
            IsActive = p.IsActive,
            Version = p.Version,
            State = p.State
        };
    }

    private static ActionResultDto JobNotFound(long id)
    {
        return ActionResultDto.NotFound($"Job #{id} not found. It may have run already.");
    }

    private static ActionResultDto PeriodicJobNotFound(string id)
    {
        return ActionResultDto.NotFound($"Periodic job '{id}' not found.");
    }
}
