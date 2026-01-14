using Microsoft.EntityFrameworkCore;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Services;

public class XactJobsUIService<TDbContext> : IXactJobsUIService
    where TDbContext : DbContext
{
    private readonly TDbContext db;

    public XactJobsUIService(TDbContext dbContext)
    {
        db = dbContext;
    }

    public async Task<DashboardStatsDto> GetDashboardStatsAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var last24h = now.AddHours(-24);

        var pendingJobs = await db.Set<XactJob>()
            .CountAsync(j => j.LeasedUntil == null || j.LeasedUntil < now, ct);

        var processingJobs = await db.Set<XactJob>()
            .CountAsync(j => j.LeasedUntil != null && j.LeasedUntil >= now, ct);

        var historyLast24h = await db.Set<XactJobHistory>()
            .Where(h => h.ProcessedAt >= last24h)
            .GroupBy(h => h.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var succeeded = historyLast24h.FirstOrDefault(x => x.Status == XactJobStatus.Completed)?.Count ?? 0;
        var failed = historyLast24h.FirstOrDefault(x => x.Status == XactJobStatus.Failed)?.Count ?? 0;
        var total = succeeded + failed;

        var periodicStats = await db.Set<XactJobPeriodic>()
            .GroupBy(p => p.IsActive)
            .Select(g => new { IsActive = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new DashboardStatsDto
        {
            PendingJobs = pendingJobs,
            ProcessingJobs = processingJobs,
            SucceededLast24h = succeeded,
            FailedLast24h = failed,
            PeriodicJobsActive = periodicStats.FirstOrDefault(x => x.IsActive)?.Count ?? 0,
            PeriodicJobsInactive = periodicStats.FirstOrDefault(x => !x.IsActive)?.Count ?? 0,
            SuccessRateLast24h = total > 0 ? (double)succeeded / total * 100 : 100
        };
    }

    public async Task<IReadOnlyList<JobCountsOverTimeDto>> GetJobCountsOverTimeAsync(
        DateTime from, DateTime to, TimeSpan interval, CancellationToken ct = default)
    {
        var history = await db.Set<XactJobHistory>()
            .Where(h => h.ProcessedAt >= from && h.ProcessedAt <= to)
            .Select(h => new { h.ProcessedAt, h.Status })
            .ToListAsync(ct);

        var results = new List<JobCountsOverTimeDto>();
        var current = from;

        while (current < to)
        {
            var next = current + interval;
            var bucket = history.Where(h => h.ProcessedAt >= current && h.ProcessedAt < next).ToList();

            results.Add(new JobCountsOverTimeDto
            {
                Timestamp = current,
                Succeeded = bucket.Count(h => h.Status == XactJobStatus.Completed),
                Failed = bucket.Count(h => h.Status == XactJobStatus.Failed),
                Cancelled = bucket.Count(h => h.Status == XactJobStatus.Cancelled),
                Skipped = bucket.Count(h => h.Status == XactJobStatus.Skipped)
            });

            current = next;
        }

        return results;
    }

    public async Task<PagedResultDto<JobDto>> GetScheduledJobsAsync(
        int page, int pageSize, string? queue = null, CancellationToken ct = default)
    {
        var query = db.Set<XactJob>().AsNoTracking();

        if (!string.IsNullOrEmpty(queue))
        {
            query = query.Where(j => j.Queue == queue);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(j => j.ScheduledAt)
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

    public async Task<bool> DeleteJobAsync(long id, CancellationToken ct = default)
    {
        var deleted = await db.Set<XactJob>()
            .Where(j => j.Id == id)
            .ExecuteDeleteAsync(ct);

        return deleted > 0;
    }

    public async Task<bool> RequeueJobAsync(long id, CancellationToken ct = default)
    {
        var updated = await db.Set<XactJob>()
            .Where(j => j.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.ScheduledAt, DateTime.UtcNow)
                .SetProperty(j => j.LeasedUntil, (DateTime?)null)
                .SetProperty(j => j.Leaser, (Guid?)null)
                .SetProperty(j => j.ErrorCount, 0), ct);

        return updated > 0;
    }

    public async Task<PagedResultDto<JobHistoryDto>> GetSucceededJobsAsync(
        int page, int pageSize, string? queue = null, CancellationToken ct = default)
    {
        return await GetHistoryByStatusAsync(XactJobStatus.Completed, page, pageSize, queue, ct);
    }

    public async Task<PagedResultDto<JobHistoryDto>> GetFailedJobsAsync(
        int page, int pageSize, string? queue = null, CancellationToken ct = default)
    {
        return await GetHistoryByStatusAsync(XactJobStatus.Failed, page, pageSize, queue, ct);
    }

    private async Task<PagedResultDto<JobHistoryDto>> GetHistoryByStatusAsync(
        XactJobStatus status, int page, int pageSize, string? queue, CancellationToken ct)
    {
        var query = db.Set<XactJobHistory>()
            .AsNoTracking()
            .Where(h => h.Status == status);

        if (!string.IsNullOrEmpty(queue))
        {
            query = query.Where(h => h.Queue == queue);
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(h => h.ProcessedAt)
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

    public async Task<bool> RetryFailedJobAsync(long id, CancellationToken ct = default)
    {
        var history = await db.Set<XactJobHistory>()
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == id && h.Status == XactJobStatus.Failed, ct);

        if (history == null)
            return false;

        var newJob = new XactJob(
            id: 0,
            scheduledAt: DateTime.UtcNow,
            typeName: history.TypeName,
            methodName: history.MethodName,
            methodArgs: history.MethodArgs,
            queue: history.Queue,
            periodicJobId: history.PeriodicJobId,
            cronExpression: history.CronExpression,
            periodicJobVersion: history.PeriodicJobVersion,
            errorCount: 0
        );

        db.Set<XactJob>().Add(newJob);
        await db.SaveChangesAsync(ct);

        return true;
    }

    public async Task<PagedResultDto<PeriodicJobDto>> GetPeriodicJobsAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.Set<XactJobPeriodic>().AsNoTracking();

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PeriodicJobDto
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
            })
            .ToListAsync(ct);

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
        return await db.Set<XactJobPeriodic>()
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Select(p => new PeriodicJobDto
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
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> TogglePeriodicJobAsync(string id, bool isActive, CancellationToken ct = default)
    {
        // Use raw SQL update since IsActive setter is private
        var updated = await db.Set<XactJobPeriodic>()
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(x => x.SetProperty(x => x.IsActive, x => !x.IsActive), ct);

        return updated > 0;
    }

    public async Task<bool> TriggerPeriodicJobNowAsync(string id, CancellationToken ct = default)
    {
        var periodicJob = await db.Set<XactJobPeriodic>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

        if (periodicJob == null)
            return false;

        var newJob = new XactJob(
            id: 0,
            scheduledAt: DateTime.UtcNow,
            typeName: periodicJob.TypeName,
            methodName: periodicJob.MethodName,
            methodArgs: periodicJob.MethodArgs,
            queue: periodicJob.Queue,
            periodicJobId: periodicJob.Id,
            cronExpression: periodicJob.CronExpression,
            periodicJobVersion: periodicJob.Version,
            errorCount: 0
        );

        db.Set<XactJob>().Add(newJob);
        await db.SaveChangesAsync(ct);

        return true;
    }

    public async Task<bool> DeletePeriodicJobAsync(string id, CancellationToken ct = default)
    {
        return await db.JobDeletePeriodicAsync(id, ct);
    }
}
