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

        /// <summary>
        /// Starts a new batch: jobs whose continuations start when all of them are finished.
        /// The jobs of a batch must be saved together, with one SaveChanges.
        /// </summary>
        public static XactJobBatch JobBatch(this DbContext dbContext)
        {
            return new XactJobBatch(dbContext, Guid.NewGuid(), isNew: true);
        }

        /// <summary>
        /// A batch that's already saved, to add continuations to it. If it's finished, they start right away.
        /// A batch id that doesn't exist is a finished batch without jobs.
        /// </summary>
        public static XactJobBatch JobBatch(this DbContext dbContext, Guid batchId)
        {
            return new XactJobBatch(dbContext, batchId, isNew: false);
        }

        internal static XactJob JobAdd(this DbContext dbContext, LambdaExpression lambdaExp, DateTime? scheduledAt, string? queue, Guid? batchId = null)
        {
            if (scheduledAt == XactJobBase.AwaitingBatchScheduledAt)
            {
                throw new ArgumentOutOfRangeException(nameof(scheduledAt), scheduledAt, "This scheduled time is reserved for continuations that wait for their batch.");
            }

            var job = XactJobSerializer.FromExpression(lambdaExp, scheduledAt, queue, batchId);

            dbContext.Set<XactJob>().Add(job);

            job.AddedTo = dbContext;

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

            return AddOrUpdatePeriodic(db, periodicJob, lambdaExp, id, cronExp, queue);
        }
        
        internal static XactJobPeriodic JobAddOrUpdatePeriodic(this DbContext db,
                                                               LambdaExpression lambdaExp,
                                                               string id,
                                                               string cronExp,
                                                               string? queue)
        {
            var periodicJob = db.Set<XactJobPeriodic>()
                .FirstOrDefault(j => j.Id == id);

            return AddOrUpdatePeriodic(db, periodicJob, lambdaExp, id, cronExp, queue);
        }

        /// <summary>
        /// A new periodic job starts active. An existing one keeps its active state, so a job paused with
        /// <see cref="JobSetPeriodicActiveAsync"/> stays paused when the application starts again.
        /// A paused job has no pending run: the next one is scheduled when it's resumed.
        /// </summary>
        private static XactJobPeriodic AddOrUpdatePeriodic(DbContext db,
                                                           XactJobPeriodic? periodicJob,
                                                           LambdaExpression lambdaExp,
                                                           string id,
                                                           string cronExp,
                                                           string? queue)
        {
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

                    if (periodicJob.IsActive)
                    {
                        ScheduleNextRun(db, periodicJob);
                    }
                }
            }

            return periodicJob;
        }

        /// <summary>
        /// Pauses or resumes a periodic job. Like the other methods, it only changes the context: the changes are saved with SaveChanges.
        /// <para>
        /// Pausing removes the pending runs that are not running at the moment, so a paused job doesn't fill the history with skipped runs.
        /// A run in progress finishes, and doesn't schedule the next one if the job is still paused by then.
        /// Resuming schedules the next run, unless there already is one.
        /// </para>
        /// The active state is kept when the periodic job is ensured again, e.g. when the application starts.
        /// </summary>
        /// <returns>False if the periodic job doesn't exist.</returns>
        public static async Task<bool> JobSetPeriodicActiveAsync(this DbContext dbContext, string id, bool isActive, CancellationToken cancellationToken)
        {
            var periodicJob = await dbContext.Set<XactJobPeriodic>()
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                .ConfigureAwait(false);

            if (periodicJob == null) return false;

            if (periodicJob.IsActive == isActive) return true;

            periodicJob.Activate(isActive);

            var version = periodicJob.Version;

            if (isActive)
            {
                var hasPendingRun = await dbContext.Set<XactJob>()
                    .AnyAsync(j => j.PeriodicJobId == id && (j.PeriodicJobVersion ?? 1) == version, cancellationToken)
                    .ConfigureAwait(false);

                if (!hasPendingRun)
                {
                    ScheduleNextRun(dbContext, periodicJob);
                }
            }
            else
            {
                var now = DateTime.UtcNow;

                // a row claimed after this read gets deleted anyway: the runner then fails to save it, and rolls the run back
                var pendingRuns = await dbContext.Set<XactJob>()
                    .Where(j => j.PeriodicJobId == id && (j.LeasedUntil == null || j.LeasedUntil < now))
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                dbContext.Set<XactJob>().RemoveRange(pendingRuns);
            }

            return true;
        }

        /// <summary>
        /// Adds the retry of a job. It keeps the time the job was originally scheduled for: see <see cref="XactJobBase.GetOriginallyScheduledAt"/>.
        /// </summary>
        internal static XactJob Reschedule(this DbContext dbContext, XactJob job, DateTime scheduledAt, int errorCount, DateTime originallyScheduledAt)
        {
            var newJob = new XactJob(0,
                                     scheduledAt,
                                     job.TypeName,
                                     job.MethodName,
                                     job.MethodArgs,
                                     job.Queue,
                                     job.PeriodicJobId,
                                     job.CronExpression,
                                     job.PeriodicJobVersion,
                                     errorCount)
            {
                // a retry stays in its batch, and a retry of a continuation is still one, but doesn't wait anymore
                BatchId = job.BatchId,
                AwaitsBatchId = job.AwaitsBatchId,
                ContinuationCondition = job.ContinuationCondition,
                OriginallyScheduledAt = originallyScheduledAt
            };

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
                                  periodicJob.CronExpression,
                                  periodicJob.Version);

            dbContext.Set<XactJob>().Add(job);

            return job;
        }
    }
}
