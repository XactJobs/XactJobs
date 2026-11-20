using System.Data;
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
    /// The most rows the lists count: a count reads every row it counts, and a backlog or a long history can have millions.
    /// </summary>
    public const int MaxCount = 10_000;

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

    /// <summary>
    /// Without the periodic runs the worker will skip: of a periodic job deleted, or whose definition changed since (another
    /// version). A paused job's pending run is deleted when it's paused. By the periodic table's primary key.
    /// </summary>
    private IQueryable<XactJob> WithoutRunsToSkip(IQueryable<XactJob> query)
    {
        return query.Where(j => j.PeriodicJobId == null
            || db.Set<XactJobPeriodic>().Any(p => p.Id == j.PeriodicJobId && p.Version == j.PeriodicJobVersion));
    }

    public async Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var last24h = now.AddHours(-24);

        var awaiting = XactJobBase.AwaitingBatchScheduledAt;

        // one read of each table, with a count per condition (COUNT(CASE WHEN ... THEN 1 END)). No rows: no group
        var jobs = await WithoutRunsToSkip(db.Set<XactJob>())
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Processing = g.Count(j => j.LeasedUntil != null && j.LeasedUntil >= now),
                Due = g.Count(j => (j.LeasedUntil == null || j.LeasedUntil < now) && j.ScheduledAt <= now),
                Scheduled = g.Count(j => (j.LeasedUntil == null || j.LeasedUntil < now) && j.ScheduledAt > now && j.ScheduledAt != awaiting),
                Awaiting = g.Count(j => (j.LeasedUntil == null || j.LeasedUntil < now) && j.ScheduledAt == awaiting),
            })
            .FirstOrDefaultAsync(ct);

        var historyLast24h = await db.Set<XactJobHistory>()
            .Where(h => h.ProcessedAt >= last24h)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Succeeded = g.Count(h => h.Status == XactJobStatus.Completed),
                Failed = g.Count(h => h.Status == XactJobStatus.Cancelled && h.ErrorStackTrace != null),
                Retried = g.Count(h => h.Status == XactJobStatus.Failed),
            })
            .FirstOrDefaultAsync(ct);

        var succeeded = historyLast24h?.Succeeded ?? 0;
        var failed = historyLast24h?.Failed ?? 0;
        var finished = succeeded + failed;

        // a list, because the Oracle provider can't translate IsActive (it has a value converter) in other ways
        var periodicActive = await db.Set<XactJobPeriodic>()
            .Select(p => p.IsActive)
            .ToListAsync(ct);

        return new DashboardStatsDto
        {
            DueJobs = jobs?.Due ?? 0,
            ScheduledJobs = jobs?.Scheduled ?? 0,
            AwaitingContinuations = jobs?.Awaiting ?? 0,
            ProcessingJobs = jobs?.Processing ?? 0,
            SucceededLast24h = succeeded,
            FailedLast24h = failed,
            RetriedLast24h = historyLast24h?.Retried ?? 0,
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

        // one index seek per queue: a DISTINCT would read the whole job table
        queues.UnionWith(await db.JobGetQueuesAsync(ct));
        queues.UnionWith(await db.Set<XactJobPeriodic>().Select(p => p.Queue).Distinct().ToListAsync(ct));

        // the default queue first
        return [QueueNames.Default, .. queues.Where(q => q != QueueNames.Default)];
    }

    public async Task<PagedResultDto<JobDto>> GetScheduledJobsAsync(
        string? queue, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Normalize(page, pageSize);

        // read in the primary key's order (queue, scheduled time, id), without sorting the whole table: all the queues are
        // listed queue by queue
        var query = db.Set<XactJob>().AsNoTracking();

        if (!string.IsNullOrEmpty(queue))
        {
            query = query.Where(j => j.Queue == queue);
        }

        if (JobSearch.Parse(search) is { } jobSearch)
        {
            query = jobSearch.Apply(query);
        }

        query = WithoutRunsToSkip(query);

        var (totalCount, isCapped) = await CountAsync(query, ct);

        var ordered = string.IsNullOrEmpty(queue)
            ? query.OrderBy(j => j.Queue).ThenBy(j => j.ScheduledAt)
            : query.OrderBy(j => j.ScheduledAt);

        var items = await ordered
            .ThenBy(j => j.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToJobDto)
            .ToListAsync(ct);

        return new PagedResultDto<JobDto>
        {
            Items = await WithBatchJobsLeftAsync(items, ct),
            TotalCount = totalCount,
            IsTotalCountCapped = isCapped,
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

    public async Task<CursorPageDto<JobHistoryDto>> GetHistoryAsync(
        HistoryCategory category, DateTime from, DateTime? to, string? queue, string? search, string? cursor, int pageSize, CancellationToken ct = default)
    {
        (_, pageSize) = Normalize(1, pageSize);

        // the period first: the index of the processed time
        var query = InPeriod(db.Set<XactJobHistory>().AsNoTracking(), from, to);

        query = ApplyCategory(query, category);

        if (!string.IsNullOrEmpty(queue))
        {
            query = query.Where(h => h.Queue == queue);
        }

        if (JobSearch.Parse(search) is { } jobSearch)
        {
            query = jobSearch.Apply(query);
        }

        var (totalCount, isCapped) = await CountAsync(query, ct);

        var page = await ReadPageAsync(PageCursor.Parse(cursor), pageSize,
            newest: () => query.OrderByDescending(h => h.ProcessedAt).ThenByDescending(h => h.Id),
            olderThan: (at, id) => query
                .Where(h => h.ProcessedAt <= at && (h.ProcessedAt < at || h.Id < id))
                .OrderByDescending(h => h.ProcessedAt).ThenByDescending(h => h.Id),
            newerThan: (at, id) => query
                .Where(h => h.ProcessedAt >= at && (h.ProcessedAt > at || h.Id > id))
                .OrderBy(h => h.ProcessedAt).ThenBy(h => h.Id),
            read: (rows, count) => rows.Take(count).Select(ToJobHistoryDto).ToListAsync(ct),
            keyOf: h => (h.ProcessedAt, h.Id));

        return page with { TotalCount = totalCount, IsTotalCountCapped = isCapped };
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

        // a continuation whose condition wasn't met, or a job a filter skipped; not a periodic run, skipped while its job was paused
        var isSkippedJob = history.Status == XactJobStatus.Skipped && history.PeriodicJobId == null;

        if (history.Status != XactJobStatus.Cancelled && !isSkippedJob)
        {
            return ActionResultDto.Conflict($"Job #{historyId} can't be retried: only failed and cancelled jobs can, and skipped jobs that aren't runs of a periodic job.");
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
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Normalize(page, pageSize);

        var query = db.Set<XactJobPeriodic>().AsNoTracking();

        if (JobSearch.Parse(search) is { } jobSearch)
        {
            query = jobSearch.Apply(query);
        }

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

        if (!isActive)
        {
            await db.SaveChangesAsync(ct);
        }
        else if (!await TrySavePeriodicRunAsync(ct))
        {
            return ActionResultDto.Conflict($"Periodic job '{id}' got a pending run at the same time: it was resumed already.");
        }

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

        if (!await TrySavePeriodicRunAsync(ct))
        {
            return ActionResultDto.Conflict($"Periodic job '{id}' got a pending run at the same time. Trigger it again to run it now.");
        }

        NotifyQueue(job.Queue);

        return ActionResultDto.Done($"Periodic job '{id}' triggered as job #{job.Id}.");
    }

    /// <summary>
    /// Saves a new run of a periodic job. The job table has one pending run per periodic job and version (a unique index): if another
    /// one was added at the same time (by another request, the app, or the scheduler's reconciliation), the save fails.
    /// </summary>
    /// <returns>False if it failed because of that run.</returns>
    private async Task<bool> TrySavePeriodicRunAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);

            return true;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueKeyViolation())
        {
            db.ChangeTracker.Clear();

            return false;
        }
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

            // an explicit level: a pooled connection keeps the one an app's transaction left on it (SqlClient doesn't reset it)
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);

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

    public async Task<PagedResultDto<BatchDto>> GetActiveBatchesAsync(int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = Normalize(page, pageSize);

        var groups = db.Set<XactJob>()
            .Where(j => j.BatchId != null)
            .GroupBy(j => j.BatchId)
            .Select(g => new { Id = g.Key, FirstAt = g.Min(j => j.ScheduledAt) });

        var (totalCount, isCapped) = await CountAsync(groups, ct);

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
            IsTotalCountCapped = isCapped,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<CursorPageDto<BatchDto>> GetFinishedBatchesAsync(
        DateTime from, DateTime? to, string? cursor, int pageSize, CancellationToken ct = default)
    {
        (_, pageSize) = Normalize(1, pageSize);

        var jobs = db.Set<XactJob>();
        var history = db.Set<XactJobHistory>();

        // the batches with a row in the period (the index of the processed time), then all the rows of each (the batch index): a
        // batch is in the period if its last row is, and its rows outside of it still count
        var batchesInPeriod = InPeriod(history, from, to)
            .Where(h => h.BatchId != null)
            .Select(h => h.BatchId);

        var groups = history
            .Where(h => h.BatchId != null && batchesInPeriod.Contains(h.BatchId) && !jobs.Any(j => j.BatchId == h.BatchId))
            .GroupBy(h => h.BatchId)
            // the smallest job id tells batches with the same last time apart: a job is in one batch
            .Select(g => new BatchKey { Id = g.Key!.Value, LastAt = g.Max(h => h.ProcessedAt), FirstJobId = g.Min(h => h.Id) });

        groups = to == null
            ? groups.Where(g => g.LastAt >= from)
            : groups.Where(g => g.LastAt >= from && g.LastAt < to);

        var (totalCount, isCapped) = await CountAsync(groups, ct);

        var page = await ReadPageAsync(PageCursor.Parse(cursor), pageSize,
            newest: () => groups.OrderByDescending(g => g.LastAt).ThenByDescending(g => g.FirstJobId),
            olderThan: (at, id) => groups
                .Where(g => g.LastAt <= at && (g.LastAt < at || g.FirstJobId < id))
                .OrderByDescending(g => g.LastAt).ThenByDescending(g => g.FirstJobId),
            newerThan: (at, id) => groups
                .Where(g => g.LastAt >= at && (g.LastAt > at || g.FirstJobId > id))
                .OrderBy(g => g.LastAt).ThenBy(g => g.FirstJobId),
            read: (rows, count) => rows.Take(count).ToListAsync(ct),
            keyOf: g => (g.LastAt, g.FirstJobId));

        var summaries = await GetBatchSummariesAsync(page.Items.Select(g => g.Id).ToList(), ct);

        return new CursorPageDto<BatchDto>
        {
            Items = page.Items.Select(g => summaries[g.Id]).ToList(),
            TotalCount = totalCount,
            IsTotalCountCapped = isCapped,
            PageSize = pageSize,
            NewerCursor = page.NewerCursor,
            OlderCursor = page.OlderCursor
        };
    }

    private sealed class BatchKey
    {
        public Guid Id { get; init; }
        public DateTime LastAt { get; init; }
        public long FirstJobId { get; init; }
    }

    /// <summary>The rows processed from <paramref name="from"/>, until <paramref name="to"/> (excluded) if it's set.</summary>
    private static IQueryable<XactJobHistory> InPeriod(IQueryable<XactJobHistory> query, DateTime from, DateTime? to)
    {
        return to == null
            ? query.Where(h => h.ProcessedAt >= from)
            : query.Where(h => h.ProcessedAt >= from && h.ProcessedAt < to.Value);
    }

    /// <summary>The rows, up to <see cref="MaxCount"/>: a count reads every row it counts.</summary>
    private static async Task<(int Count, bool IsCapped)> CountAsync<T>(IQueryable<T> query, CancellationToken ct)
    {
        var count = await query.Take(MaxCount + 1).CountAsync(ct);

        return (Math.Min(count, MaxCount), count > MaxCount);
    }

    /// <summary>
    /// A page of rows ordered by a time and an id, newest first, after the row of the cursor: the next older rows, or the next newer
    /// ones (read oldest first, then reversed). A page of newer rows that reaches the newest ones is the newest page, which shows rows
    /// that arrive later too, on the next refresh. Reads one row more than the page, to know if there are more.
    /// </summary>
    private static async Task<CursorPageDto<T>> ReadPageAsync<TRow, T>(PageCursor? cursor,
                                                                       int pageSize,
                                                                       Func<IQueryable<TRow>> newest,
                                                                       Func<DateTime, long, IQueryable<TRow>> olderThan,
                                                                       Func<DateTime, long, IQueryable<TRow>> newerThan,
                                                                       Func<IQueryable<TRow>, int, Task<List<T>>> read,
                                                                       Func<T, (DateTime At, long Id)> keyOf)
    {
        if (cursor is { IsNewer: true } newer)
        {
            var rows = await read(newerThan(newer.At, newer.Id), pageSize + 1);

            if (rows.Count > pageSize)
            {
                rows.RemoveAt(pageSize);
                rows.Reverse();

                return new CursorPageDto<T>
                {
                    Items = rows,
                    PageSize = pageSize,
                    NewerCursor = Cursor(PageCursor.Newer, keyOf(rows[0])),
                    OlderCursor = Cursor(PageCursor.Older, keyOf(rows[^1]))
                };
            }

            cursor = null;
        }

        var page = await read(cursor is { } older ? olderThan(older.At, older.Id) : newest(), pageSize + 1);

        var hasOlder = page.Count > pageSize;

        if (hasOlder) page.RemoveAt(pageSize);

        return new CursorPageDto<T>
        {
            Items = page,
            PageSize = pageSize,
            NewerCursor = cursor == null ? null : Cursor(PageCursor.Newer, page.Count > 0 ? keyOf(page[0]) : (cursor.Value.At, cursor.Value.Id)),
            OlderCursor = hasOlder ? Cursor(PageCursor.Older, keyOf(page[^1])) : null
        };

        static string Cursor(Func<DateTime, long, PageCursor> create, (DateTime At, long Id) key) => create(key.At, key.Id).ToString();
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
            TimeZone = p.TimeZone,
            Exclusions = p.Exclusions,
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
