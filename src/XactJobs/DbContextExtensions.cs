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

        public static Task JobEnsurePeriodicAsync(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Action> jobExpression, string? queue, bool isActive, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, queue, isActive, cancellationToken);
        }

        public static Task JobEnsurePeriodicAsync<T>(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Action<T>> jobExpression, string? queue, bool isActive, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, queue, isActive, cancellationToken);
        }

        public static Task JobEnsurePeriodicAsync(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Func<Task>> jobExpression, string? queue, bool isActive, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, queue, isActive, cancellationToken);
        }

        public static Task JobSchedulePeriodic<T>(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue, bool isActive, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, queue, isActive, cancellationToken);
        }

        public static Task JobEnsurePeriodicAsync(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Action> jobExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, null, true, cancellationToken);
        }

        public static Task JobEnsurePeriodicAsync<T>(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Action<T>> jobExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, null, true, cancellationToken);
        }

        public static Task JobEnsurePeriodicAsync(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Func<Task>> jobExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, null, true, cancellationToken);
        }

        public static Task JobSchedulePeriodic<T>(this DbContext dbContext, string id, string cronExpression, [InstantHandle] Expression<Func<T, Task>> jobExpression, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, cronExpression, null, true, cancellationToken);
        }

        public static async Task<bool> JobDeletePeriodicAsync(this DbContext dbContext, string id, CancellationToken cancellationToken)
        {
            var periodicJob = await dbContext.Set<XactJobPeriodic>()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (periodicJob == null) return false;

            // no need to delete jobs, they will be skipped by the runner, if periodic job definition does not exist
            //
            // await RemoveQueuedJobs(dbContext, periodicJob, cancellationToken)
            //    .ConfigureAwait(false);

            dbContext.Set<XactJobPeriodic>()
                .Remove(periodicJob);

            return true;
        }

        /*
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
        */

        private static XactJob JobAdd(DbContext dbContext, LambdaExpression lambdaExp, DateTime? scheduledAt, string? queue)
        {
            var job = XactJobSerializer.FromExpression(lambdaExp, scheduledAt, queue);

            dbContext.Set<XactJob>().Add(job);

            return job;
        }

        internal static async Task JobAddOrUpdatePeriodicAsync(this DbContext db,
                                                               LambdaExpression lambdaExp,
                                                               string id,
                                                               string cronExp,
                                                               string? queue,
                                                               bool isActive,
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
                    // existing job will be skipped by the runner (it will be detected as incompatible)
                    // so no need to modify it here
                    //
                    // await RemoveQueuedJobs(db, periodicJob, cancellationToken)
                    //    .ConfigureAwait(false);

                    periodicJob.UpdateDefinition(templateJob);

                    ScheduleNextRun(db, periodicJob);
                }
            }

            periodicJob.Activate(isActive);
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
