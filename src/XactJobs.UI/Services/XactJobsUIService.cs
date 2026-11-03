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

        var scheduledJobs = await db.Set<XactJob>()
            .CountAsync(j => (j.LeasedUntil == null || j.LeasedUntil < now) && j.ScheduledAt > now, ct);

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
            .Select(j => new JobDto
            {
                Id = j.Id,
                ScheduledAt = j.ScheduledAt,
                TypeName = j.TypeName,
                MethodName = j.MethodName,
                Queue = j.Queue,
                ErrorCount = j.ErrorCount,
                LeasedUntil = j.LeasedUntil,
                Leaser = j.Leaser,
                PeriodicJobId = j.PeriodicJobId
            })
            .ToListAsync(ct);

        return new PagedResultDto<JobDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<JobDto?> GetJobByIdAsync(long id, CancellationToken ct = default)
    {
        return await db.Set<XactJob>()
            .AsNoTracking()
            .Where(j => j.Id == id)
            .Select(j => new JobDto
            {
                Id = j.Id,
                ScheduledAt = j.ScheduledAt,
                TypeName = j.TypeName,
                MethodName = j.MethodName,
                Queue = j.Queue,
                ErrorCount = j.ErrorCount,
                LeasedUntil = j.LeasedUntil,
                Leaser = j.Leaser,
                PeriodicJobId = j.PeriodicJobId
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ActionResultDto> CancelJobAsync(long id, string? cancelledBy, CancellationToken ct = default)
    {
        var job = await FindJobAsync(id, ct);

        if (job == null) return JobNotFound(id);

        if (job.PeriodicJobId != null)
        {
            return ActionResultDto.Conflict($"Job #{id} is a run of the periodic job '{job.PeriodicJobId}'. Pause the periodic job instead.");
        }

        var message = string.IsNullOrEmpty(cancelledBy) ? CancelledMessage : $"{CancelledMessage} by {cancelledBy}";

        var conflict = await ReplacePendingJobAsync(job, now =>
        {
            db.Set<XactJobHistory>().Add(new XactJobHistory(job.Id,
                                                            now,
                                                            XactJobStatus.Cancelled,
                                                            job.ScheduledAt,
                                                            job.TypeName,
                                                            job.MethodName,
                                                            job.MethodArgs,
                                                            job.Queue,
                                                            job.PeriodicJobId,
                                                            job.CronExpression,
                                                            job.PeriodicJobVersion,
                                                            job.ErrorCount,
                                                            message,
                                                            errorStackTrace: null));
        }, ct);

        return conflict ?? ActionResultDto.Done($"Job #{id} cancelled.");
    }

    public async Task<ActionResultDto> RunJobNowAsync(long id, CancellationToken ct = default)
    {
        var job = await FindJobAsync(id, ct);

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
            .Select(h => new JobHistoryDto
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
                PeriodicJobId = h.PeriodicJobId
            })
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
            .Select(h => new JobHistoryDto
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
                PeriodicJobId = h.PeriodicJobId
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ActionResultDto> RetryJobAsync(long historyId, CancellationToken ct = default)
    {
        var history = await db.Set<XactJobHistory>()
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == historyId, ct);

        if (history == null) return ActionResultDto.NotFound($"Job #{historyId} not found in the history.");

        if (history.Status != XactJobStatus.Cancelled)
        {
            return ActionResultDto.Conflict($"Job #{historyId} can't be retried: only failed and cancelled jobs can.");
        }

        // a one-off job: as a run of the periodic job, it would schedule a second chain of runs
        var newJob = new XactJob(0,
                                 DateTime.UtcNow,
                                 history.TypeName,
                                 history.MethodName,
                                 history.MethodArgs,
                                 history.Queue);

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

        if (job.ScheduledAt <= DateTime.UtcNow)
        {
            NotifyQueue(job.Queue);

            return ActionResultDto.Done($"{subject} is already due, it starts as soon as a worker is free.");
        }

        XactJob? newJob = null;

        // the scheduled time is part of the primary key, so the row is replaced instead of updated
        var conflict = await ReplacePendingJobAsync(job, now =>
        {
            newJob = new XactJob(0,
                                 now,
                                 job.TypeName,
                                 job.MethodName,
                                 job.MethodArgs,
                                 job.Queue,
                                 job.PeriodicJobId,
                                 job.CronExpression,
                                 job.PeriodicJobVersion,
                                 job.ErrorCount);

            db.Set<XactJob>().Add(newJob);
        }, ct);

        if (conflict != null) return conflict;

        NotifyQueue(job.Queue);

        return ActionResultDto.Done($"{subject} runs now, as job #{newJob!.Id}.");
    }

    /// <summary>
    /// Deletes the job row if no worker has leased it, and adds what replaces it, in one transaction.
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

            // the whole primary key, so the delete can use it
            var deleted = await db.Set<XactJob>()
                .Where(j => j.Queue == job.Queue
                    && j.ScheduledAt == job.ScheduledAt
                    && j.Id == job.Id
                    && (j.LeasedUntil == null || j.LeasedUntil < now))
                .ExecuteDeleteAsync(token);

            if (deleted == 0) return false;

            addReplacement(now);

            await db.SaveChangesAsync(token);

            await transaction.CommitAsync(token);

            return true;
        }, ct);

        return replaced ? null : ActionResultDto.Conflict($"Job #{job.Id} started running or finished meanwhile.");
    }

    private Task<XactJob?> FindJobAsync(long id, CancellationToken ct)
    {
        return db.Set<XactJob>()
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id, ct);
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
            Version = p.Version
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
