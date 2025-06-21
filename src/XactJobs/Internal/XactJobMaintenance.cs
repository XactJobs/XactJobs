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
            var deleteBeforeUtc = DateTime.UtcNow.Subtract(TimeSpan.FromDays(_options.HistoryRetentionPeriodInDays));

            await _db.Set<XactJobHistory>()
                .Where(x => x.ProcessedAt < deleteBeforeUtc)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
