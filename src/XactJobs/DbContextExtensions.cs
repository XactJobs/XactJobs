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

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using XactJobs.Annotations;
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

        public static XactJobPeriodic JobEnsurePeriodic(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression, string id, string queue, string cronExpression)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, cronExpression, queue);
        }

        public static XactJobPeriodic JobEnsurePeriodic<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression, string id, string queue, string cronExpression)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, cronExpression, queue);
        }

        public static XactJobPeriodic JobEnsurePeriodic(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression, string id, string queue, string cronExpression)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, cronExpression, queue);
        }

        public static XactJobPeriodic JobEnsurePeriodic<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression, string id, string queue, string cronExpression)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, cronExpression, queue);
        }

        public static XactJobPeriodic JobEnsurePeriodic(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression, string id, string cronExpression)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, cronExpression, null);
        }

        public static XactJobPeriodic JobEnsurePeriodic<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression, string id, string cronExpression)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, cronExpression, null);
        }

        public static XactJobPeriodic JobEnsurePeriodic(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression, string id, string cronExpression)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, cronExpression, null);
        }

        public static XactJobPeriodic JobEnsurePeriodic<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression, string id, string cronExpression)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, cronExpression, null);
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

        internal static XactJob JobAdd(this DbContext dbContext, LambdaExpression lambdaExp, DateTime? scheduledAt, string? queue)
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
        
        internal static XactJobPeriodic JobAddOrUpdatePeriodic(this DbContext db,
                                                               LambdaExpression lambdaExp,
                                                               string id,
                                                               string cronExp,
                                                               string? queue)
        {
            var periodicJob = db.Set<XactJobPeriodic>()
                .FirstOrDefault(j => j.Id == id);

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
            var cronGenerator = new CronUtil.CronSequenceGenerator(periodicJob.CronExpression, TimeZoneInfo.Utc);

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
