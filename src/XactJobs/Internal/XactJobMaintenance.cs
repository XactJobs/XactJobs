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
