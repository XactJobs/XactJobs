using Microsoft.EntityFrameworkCore;

namespace XactJobs.UI
{
    public class XactJobsDbContextAccessor(DbContext dbContext)
    {
        public DbContext DbContext { get; init; } = dbContext;
    }
}