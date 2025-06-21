using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using XactJobs.Annotations;
using XactJobs.Cron;
using XactJobs.Internal;

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

        public static XactJob JobScheduleAt(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression, DateTime scheduleAt, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleAt<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression, DateTime scheduleAt, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleAt(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression, DateTime scheduleAt, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleAt<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression, DateTime scheduleAt, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, scheduleAt, queue);
        }

        public static XactJob JobScheduleIn(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression, TimeSpan delay, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob JobScheduleIn<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression, TimeSpan delay, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob JobScheduleIn(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression, TimeSpan delay, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static XactJob JobScheduleIn<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression, TimeSpan delay, string? queue = null)
        {
            return JobAdd(dbContext, jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression, string id, string queue, string cronExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, queue, cancellationToken);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression, string id, string queue, string cronExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, queue, cancellationToken);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression, string id, string queue, string cronExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, queue, cancellationToken);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression, string id, string queue, string cronExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, queue, cancellationToken);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression, string id, string cronExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, null, cancellationToken);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression, string id, string cronExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, null, cancellationToken);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression, string id, string cronExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, null, cancellationToken);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression, string id, string cronExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, null, cancellationToken);
        }

        public static async Task<bool> JobDeletePeriodicAsync(this DbContext dbContext, string id, CancellationToken cancellationToken)
        {
            var periodicJob = await dbContext.Set<XactJobPeriodic>()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (periodicJob == null) return false;

            dbContext.Set<XactJobPeriodic>()
                .Remove(periodicJob);

            return true;
        }

        private static XactJob JobAdd(DbContext dbContext, LambdaExpression lambdaExp, DateTime? scheduledAt, string? queue)
        {
            var job = XactJobSerializer.FromExpression(lambdaExp, scheduledAt, queue);

            dbContext.Set<XactJob>().Add(job);

            return job;
        }

        internal static async Task<XactJobPeriodic> JobAddOrUpdatePeriodicAsync(this DbContext db,
                                                                                LambdaExpression lambdaExp,
                                                                                string id,
                                                                                string cronExp,
                                                                                string? queue,
                                                                                CancellationToken cancellationToken)
        {
            var periodicJob = await db.Set<XactJobPeriodic>()
                .FirstOrDefaultAsync(j => j.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (periodicJob == null)
            {
                periodicJob = XactJobSerializer.FromExpressionPeriodic(lambdaExp, id, cronExp, queue);

                db.Set<XactJobPeriodic>().Add(periodicJob);

                ScheduleNextRun(db, periodicJob);
            }
            else
            {
                var templateJob = XactJobSerializer.FromExpressionPeriodic(lambdaExp, id, cronExp, queue);

                if (!periodicJob.IsCompatibleWith(templateJob))
                {
                    periodicJob.UpdateDefinition(templateJob);

                    ScheduleNextRun(db, periodicJob);
                }
            }

            periodicJob.Activate(true);

            return periodicJob;
        }

        internal static XactJob Reschedule(this DbContext dbContext, XactJob job, DateTime scheduledAt, int errorCount)
        {
            var newJob = new XactJob(0,
                                     scheduledAt,
                                     job.TypeName,
                                     job.MethodName,
                                     job.MethodArgs,
                                     job.Queue,
                                     job.PeriodicJobId,
                                     job.CronExpression,
                                     errorCount);

            dbContext.Set<XactJob>().Add(newJob);

            return newJob;
        }

        internal static XactJob ScheduleNextRun(this DbContext dbContext, XactJobPeriodic periodicJob)
        {
            var cronGenerator = new CronSequenceGenerator(periodicJob.CronExpression, TimeZoneInfo.Utc);

            var nextRunUtc = cronGenerator.NextUtc(DateTime.UtcNow);

            var job = new XactJob(0,
                                  nextRunUtc,
                                  periodicJob.TypeName,
                                  periodicJob.MethodName,
                                  periodicJob.MethodArgs,
                                  periodicJob.Queue,
                                  periodicJob.Id,
                                  periodicJob.CronExpression);

            dbContext.Set<XactJob>().Add(job);

            return job;
        }
    }
}
