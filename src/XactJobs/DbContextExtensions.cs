using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using XactJobs.Annotations;
using XactJobs.Cron;

namespace XactJobs
{
    public static class DbContextExtensions
    {
        public static XactJob JobEnqueue(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, null, queue);
        }

        public static XactJob JobEnqueue<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, null, queue);
        }

        public static XactJob JobEnqueue(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, null, queue);
        }

        public static XactJob JobEnqueue<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, null, queue);
        }

        public static XactJob JobScheduleAt(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleAt<T>(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleAt(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleAt<T>(this DbContext dbContext, DateTime scheduleAt, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleIn(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob JobScheduleIn<T>(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob JobScheduleIn(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob JobScheduleIn<T>(this DbContext dbContext, TimeSpan delay, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static Task JobEnsurePeriodicAsync(this DbContext dbContext, string name, string cronExpression, [InstantHandle] Expression<Action> jobExpression, string? queue, bool isActive, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, name, cronExpression, queue, isActive, cancellationToken);
        }

        public static Task JobEnsurePeriodicAsync<T>(this DbContext dbContext, string name, string cronExpression, [InstantHandle] Expression<Action<T>> jobExpression, string? queue, bool isActive, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, name, cronExpression, queue, isActive, cancellationToken);
        }

        public static Task JobEnsurePeriodicAsync(this DbContext dbContext, string name, string cronExpression, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue, bool isActive, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, name, cronExpression, queue, isActive, cancellationToken);
        }

        public static Task JobSchedulePeriodic<T>(this DbContext dbContext, string name, string cronExpression, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue, bool isActive, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, name, cronExpression, queue, isActive, cancellationToken);
        }

        public static Task JobEnsurePeriodicAsync(this DbContext dbContext, string name, string cronExpression, [InstantHandle] Expression<Action> jobExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, name, cronExpression, null, true, cancellationToken);
        }

        public static Task JobEnsurePeriodicAsync<T>(this DbContext dbContext, string name, string cronExpression, [InstantHandle] Expression<Action<T>> jobExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, name, cronExpression, null, true, cancellationToken);
        }

        public static Task JobEnsurePeriodicAsync(this DbContext dbContext, string name, string cronExpression, [InstantHandle] Expression<Func<Task>> jobExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, name, cronExpression, null, true, cancellationToken);
        }

        public static Task JobSchedulePeriodic<T>(this DbContext dbContext, string name, string cronExpression, [InstantHandle] Expression<Func<T, Task>> jobExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, name, cronExpression, null, true, cancellationToken);
        }

        public static async Task<bool> JobDeletePeriodicAsync(this DbContext dbContext, string name, CancellationToken cancellationToken)
        {
            var periodicJob = await dbContext.Set<XactJobPeriodic>()
                .FirstOrDefaultAsync(x => x.Name == name, cancellationToken)
                .ConfigureAwait(false);

            if (periodicJob == null) return false;

            await RemoveQueuedJobs(dbContext, periodicJob, cancellationToken)
                .ConfigureAwait(false);

            dbContext.Set<XactJobPeriodic>()
                .Remove(periodicJob);

            return true;
        }

        private static async Task RemoveQueuedJobs(DbContext dbContext, XactJobPeriodic periodicJob, CancellationToken cancellationToken)
        {
            var queuedJobRuns = await dbContext.Set<XactJob>()
                                    .Where(x => x.PeriodicJobId == periodicJob.Id)
                                    .ToListAsync(cancellationToken)
                                    .ConfigureAwait(false);

            foreach (var queuedJobRun in queuedJobRuns)
            {
                dbContext.Set<XactJob>().Remove(queuedJobRun);
            }
        }

        private static XactJob JobAdd(DbContext dbContext, LambdaExpression lambdaExp, DateTime? scheduledAt, string? queue)
        {
            var dialect = dbContext.Database.ProviderName.ToSqlDialect();

            var id = dialect.NewJobId();

            var job = XactJobSerializer.FromExpression(lambdaExp, id, scheduledAt, queue);

            dbContext.Set<XactJob>().Add(job);

            return job;
        }

        internal static async Task JobAddOrUpdatePeriodicAsync(this DbContext db,
                                                               LambdaExpression lambdaExp,
                                                               string name,
                                                               string cronExp,
                                                               string? queue,
                                                               bool isActive,
                                                               CancellationToken cancellationToken)
        {
            var periodicJob = await db.Set<XactJobPeriodic>()
                .FirstOrDefaultAsync(j => j.Name == name, cancellationToken)
                .ConfigureAwait(false);

            if (periodicJob == null)
            {
                var dialect = db.Database.ProviderName.ToSqlDialect();

                var id = dialect.NewJobId();

                periodicJob = XactJobSerializer.FromExpressionPeriodic(lambdaExp, id, name, cronExp, queue);

                db.Set<XactJobPeriodic>().Add(periodicJob);

                ScheduleNextRun(db, periodicJob);
            }
            else
            {
                var templateJob = XactJobSerializer.FromExpressionPeriodic(lambdaExp, Guid.Empty, name, cronExp, queue);

                if (!periodicJob.IsCompatibleWith(templateJob))
                {
                    await RemoveQueuedJobs(db, periodicJob, cancellationToken)
                        .ConfigureAwait(false);

                    periodicJob.UpdateDefinition(templateJob);

                    ScheduleNextRun(db, periodicJob);
                }
            }

            periodicJob.Activate(isActive);
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
