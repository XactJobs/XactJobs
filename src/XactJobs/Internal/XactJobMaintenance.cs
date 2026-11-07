// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using Microsoft.EntityFrameworkCore;

namespace XactJobs.Internal
{
    internal class XactJobMaintenance<TDbContext> where TDbContext: DbContext
    {
        private readonly TDbContext _db;
        private readonly XactJobsOptions<TDbContext> _options;

        public XactJobMaintenance(TDbContext db, XactJobsOptions<TDbContext> options)
        {
            _db = db;
            _options = options;
        }

        public async Task CleanupJobHistoryAsync(CancellationToken cancellationToken)
        {
            // a run scheduled by another node, on a node without a retention period: nothing to delete
            if (_options.HistoryRetentionPeriodInDays is not { } days) return;

            var deleteBeforeUtc = DateTime.UtcNow.Subtract(TimeSpan.FromDays(days));

            var jobs = _db.Set<XactJob>();

            // a continuation that still waits for its batch needs the batch's history, to know if its jobs failed
            await _db.Set<XactJobHistory>()
                .Where(x => x.ProcessedAt < deleteBeforeUtc
                    && (x.BatchId == null || !jobs.Any(j => j.AwaitsBatchId == x.BatchId && j.ScheduledAt == XactJobBase.AwaitingBatchScheduledAt)))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
