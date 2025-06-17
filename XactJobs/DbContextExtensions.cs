using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

using XactJobs.Annotations;
using XactJobs.Cron;

namespace XactJobs
{
    public static class DbContextExtensions
    {
        public static XactJob JobEnqueue(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, null, queue);
        }

        public static XactJob JobEnqueue<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, null, queue);
        }

        public static XactJob JobEnqueue(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, null, queue);
        }

        public static XactJob JobEnqueue<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, null, queue);
        }

        public static XactJob JobScheduleAt(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleAt<T>(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleAt(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleAt<T>(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleIn(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob JobScheduleIn<T>(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob JobScheduleIn(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob JobScheduleIn<T>(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return AddJob(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJobPeriodic JobSchedulePeriodic(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return AddJobPeriodic(dbContext, jobExpression, id, cronExpression, queue);
        }

        public static XactJobPeriodic JobSchedulePeriodic<T>(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return AddJobPeriodic(dbContext, jobExpression, id, cronExpression, queue);
        }

        public static XactJobPeriodic JobSchedulePeriodic(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return AddJobPeriodic(dbContext, jobExpression, id, cronExpression, queue);
        }

        public static XactJobPeriodic JobSchedulePeriodic<T>(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return AddJobPeriodic(dbContext, jobExpression, id, cronExpression, queue);
        }

        public static async Task<bool> JobDeletePeriodicAsync(this DbContext dbContext, string name, CancellationToken cancellationToken)
        {
            var job = await dbContext.Set<XactJobPeriodic>()
                .FirstOrDefaultAsync(x => x.Name == name, cancellationToken)
                .ConfigureAwait(false);

            if (job == null) return false;

            dbContext.Set<XactJobPeriodic>()
                .Remove(job);

            return true;
        }

        private static XactJob AddJob(DbContext dbContext, LambdaExpression lambdaExp, DateTime? scheduledAt, string? queue)
        {
            var dialect = dbContext.Database.ProviderName.ToSqlDialect();

            var id = dialect.NewJobId();

            var job = XactJobSerializer.FromExpression(lambdaExp, id, scheduledAt, queue);

            dbContext.Set<XactJob>().Add(job);

            return job;
        }

        internal static XactJobPeriodic AddJobPeriodic(this DbContext dbContext, LambdaExpression lambdaExp, string name, string cronExp, string? queue)
        {
            var dialect = dbContext.Database.ProviderName.ToSqlDialect();

            var id = dialect.NewJobId();

            var job = XactJobSerializer.FromExpressionPeriodic(lambdaExp, id, name, cronExp, queue);

            dbContext.Set<XactJobPeriodic>().Add(job);

            ScheduleNextRun(dbContext, job);

            return job;
        }

        internal static XactJob ScheduleNextRun(this DbContext dbContext, XactJobPeriodic periodicJob)
        {
            var cronGenerator = new CronSequenceGenerator(periodicJob.CronExpression, TimeZoneInfo.Utc);

            var nextRunUtc = cronGenerator.NextUtc(DateTime.UtcNow);

            var dialect = dbContext.Database.ProviderName.ToSqlDialect();

            var job = new XactJob(dialect.NewJobId(),
                                  nextRunUtc,
                                  XactJobStatus.Queued,
                                  periodicJob.TypeName,
                                  periodicJob.MethodName,
                                  periodicJob.MethodArgs,
                                  periodicJob.Queue,
                                  periodicJob.Id);

            dbContext.Set<XactJob>().Add(job);

            return job;
        }
    }
}
