// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

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

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression, string id, string queue, XactCronSchedule schedule, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, schedule, queue, cancellationToken);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression, string id, string queue, XactCronSchedule schedule, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, schedule, queue, cancellationToken);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression, string id, string queue, XactCronSchedule schedule, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, schedule, queue, cancellationToken);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression, string id, string queue, XactCronSchedule schedule, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, schedule, queue, cancellationToken);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression, string id, XactCronSchedule schedule, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, schedule, null, cancellationToken);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression, string id, XactCronSchedule schedule, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, schedule, null, cancellationToken);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression, string id, XactCronSchedule schedule, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, schedule, null, cancellationToken);
        }

        public static Task<XactJobPeriodic> JobEnsurePeriodicAsync<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression, string id, XactCronSchedule schedule, CancellationToken cancellationToken)
        {
            return JobAddOrUpdatePeriodicAsync(dbContext, jobExpression, id, schedule, null, cancellationToken);
        }

        public static XactJobPeriodic JobEnsurePeriodic(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression, string id, string queue, XactCronSchedule schedule)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, schedule, queue);
        }

        public static XactJobPeriodic JobEnsurePeriodic<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression, string id, string queue, XactCronSchedule schedule)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, schedule, queue);
        }

        public static XactJobPeriodic JobEnsurePeriodic(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression, string id, string queue, XactCronSchedule schedule)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, schedule, queue);
        }

        public static XactJobPeriodic JobEnsurePeriodic<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression, string id, string queue, XactCronSchedule schedule)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, schedule, queue);
        }

        public static XactJobPeriodic JobEnsurePeriodic(this DbContext dbContext, [InstantHandle] Expression<Action> jobExpression, string id, XactCronSchedule schedule)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, schedule, null);
        }

        public static XactJobPeriodic JobEnsurePeriodic<T>(this DbContext dbContext, [InstantHandle] Expression<Action<T>> jobExpression, string id, XactCronSchedule schedule)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, schedule, null);
        }

        public static XactJobPeriodic JobEnsurePeriodic(this DbContext dbContext, [InstantHandle] Expression<Func<Task>> jobExpression, string id, XactCronSchedule schedule)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, schedule, null);
        }

        public static XactJobPeriodic JobEnsurePeriodic<T>(this DbContext dbContext, [InstantHandle] Expression<Func<T, Task>> jobExpression, string id, XactCronSchedule schedule)
        {
            return JobAddOrUpdatePeriodic(dbContext, jobExpression, id, schedule, null);
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
                                                                                XactCronSchedule schedule,
                                                                                string? queue,
                                                                                CancellationToken cancellationToken)
        {
            var periodicJob = await db.Set<XactJobPeriodic>()
                .FirstOrDefaultAsync(j => j.Id == id, cancellationToken)
                .ConfigureAwait(false);

            (periodicJob, var isExclusionsChange) = AddOrUpdatePeriodic(db, periodicJob, lambdaExp, id, schedule, queue);

            if (isExclusionsChange)
            {
                var pendingRuns = await PendingRuns(db, id).ToListAsync(cancellationToken).ConfigureAwait(false);

                ReplacePendingRuns(db, periodicJob, pendingRuns);
            }

            return periodicJob;
        }
        
        internal static XactJobPeriodic JobAddOrUpdatePeriodic(this DbContext db,
                                                               LambdaExpression lambdaExp,
                                                               string id,
                                                               XactCronSchedule schedule,
                                                               string? queue)
        {
            var periodicJob = db.Set<XactJobPeriodic>()
                .FirstOrDefault(j => j.Id == id);

            (periodicJob, var isExclusionsChange) = AddOrUpdatePeriodic(db, periodicJob, lambdaExp, id, schedule, queue);

            if (isExclusionsChange)
            {
                ReplacePendingRuns(db, periodicJob, PendingRuns(db, id).ToList());
            }

            return periodicJob;
        }

        /// <summary>
        /// A new periodic job starts active. An existing one keeps its active state, so a job paused with
        /// <see cref="JobSetPeriodicActiveAsync"/> stays paused when the application starts again.
        /// A paused job has no pending run: the next one is scheduled when it's resumed.
        /// </summary>
        /// <returns>
        /// The periodic job, and whether only the exclusions of an active one changed: its version stays, and the caller replaces its
        /// pending runs (<see cref="ReplacePendingRuns"/>), whose times the old exclusions gave.
        /// </returns>
        private static (XactJobPeriodic PeriodicJob, bool IsExclusionsChange) AddOrUpdatePeriodic(
            DbContext db, XactJobPeriodic? periodicJob, LambdaExpression lambdaExp, string id, XactCronSchedule schedule, string? queue)
        {
            if (schedule.CronExpression == null) throw new ArgumentException("The schedule has no cron expression.", nameof(schedule));

            if (periodicJob == null)
            {
                periodicJob = XactJobSerializer.FromExpressionPeriodic(lambdaExp, id, schedule, queue);

                db.Set<XactJobPeriodic>().Add(periodicJob);

                ScheduleNextRun(db, periodicJob);
            }
            else
            {
                var templateJob = XactJobSerializer.FromExpressionPeriodic(lambdaExp, id, schedule, queue);

                if (!periodicJob.IsCompatibleWith(templateJob))
                {
                    periodicJob.UpdateDefinition(templateJob);

                    if (periodicJob.IsActive)
                    {
                        ScheduleNextRun(db, periodicJob);
                    }
                }
                else if (periodicJob.Exclusions != templateJob.Exclusions)
                {
                    periodicJob.UpdateExclusions(templateJob.Exclusions);

                    return (periodicJob, periodicJob.IsActive);
                }
            }

            return (periodicJob, false);
        }

        /// <summary>
        /// The periodic job's runs that aren't running (not leased, or their lease expired): a run claimed after they're read is deleted
        /// anyway, and the worker then fails to save it, and rolls the run back.
        /// </summary>
        private static IQueryable<XactJob> PendingRuns(DbContext db, string periodicJobId)
        {
            var now = DateTime.UtcNow;

            return db.Set<XactJob>().Where(j => j.PeriodicJobId == periodicJobId && (j.LeasedUntil == null || j.LeasedUntil < now));
        }

        /// <summary>
        /// Moves the next run, as its schedule gave it (not due yet, a first attempt, not moved by Run now), to the next time the changed
        /// exclusions allow, if that's another one: deletes its row and adds the new one (EF saves the delete first, which the unique
        /// index of the periodic job's runs needs). A run that's due, a retry, a moved run, or a running one keeps its row: the worker
        /// skips it if the new exclusions leave its time out, and a running run schedules its next one with them.
        /// </summary>
        private static void ReplacePendingRuns(DbContext db, XactJobPeriodic periodicJob, List<XactJob> pendingRuns)
        {
            var now = DateTime.UtcNow;

            var nextRuns = pendingRuns
                .Where(j => (j.PeriodicJobVersion ?? 1) == periodicJob.Version && j.ScheduledAt > now && j.ErrorCount == 0 && j.OriginallyScheduledAt == null)
                .ToList();

            if (nextRuns.Count == 0) return;

            if (nextRuns.Count == 1 && nextRuns[0].ScheduledAt == NextRunUtc(periodicJob)) return;

            db.Set<XactJob>().RemoveRange(nextRuns);

            ScheduleNextRun(db, periodicJob);
        }

        /// <summary>
        /// Pauses or resumes a periodic job. Like the other methods, it only changes the context: the changes are saved with SaveChanges.
        /// <para>
        /// Pausing removes the pending runs that are not running at the moment, so a paused job doesn't fill the history with skipped runs.
        /// A run in progress finishes, and doesn't schedule the next one if the job is still paused by then.
        /// Resuming schedules the next run, unless there already is one. If it's resumed twice at the same time, the second save fails
        /// with a <see cref="DbUpdateException"/> for which <see cref="DbExceptionExtensions.IsUniqueKeyViolation"/> is true: a periodic
        /// job has at most one pending run of each version.
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
                var pendingRuns = await PendingRuns(dbContext, id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                dbContext.Set<XactJob>().RemoveRange(pendingRuns);
            }

            return true;
        }

        /// <summary>
        /// Moves a claimed job to its retry: the same row, with the new scheduled time, and without the lease. It keeps its id, and the
        /// time it was originally scheduled for: see <see cref="XactJobBase.GetOriginallyScheduledAt"/>. A retry stays in its batch, and
        /// a retry of a continuation is still one, but doesn't wait anymore (its scheduled time isn't the reserved one).
        /// The scheduled time is part of the primary key, so it's an UPDATE by the old key (EF doesn't change the key of a tracked entity).
        /// </summary>
        /// <returns>False if the row is gone: another worker processed the job after its lease expired.</returns>
        internal static async Task<bool> RescheduleAsync(this DbContext dbContext, XactJob job, DateTime scheduledAt, int errorCount, DateTime originallyScheduledAt, CancellationToken cancellationToken)
        {
            var updated = await dbContext.Set<XactJob>()
                .Where(j => j.Queue == job.Queue && j.ScheduledAt == job.ScheduledAt && j.Id == job.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.ScheduledAt, scheduledAt)
                    .SetProperty(j => j.ErrorCount, errorCount)
                    .SetProperty(j => j.OriginallyScheduledAt, originallyScheduledAt)
                    .SetProperty(j => j.Leaser, (Guid?)null)
                    .SetProperty(j => j.LeasedUntil, (DateTime?)null), cancellationToken)
                .ConfigureAwait(false);

            return updated > 0;
        }

        /// <summary>The periodic job's next time from now, in its time zone, around its exclusions.</summary>
        private static DateTime NextRunUtc(XactJobPeriodic periodicJob)
        {
            return new CronUtil.CronSequenceGenerator(periodicJob.CronExpression, CronUtil.CronTimeZones.Find(periodicJob.TimeZone), periodicJob.Exclusions)
                .NextUtc(DateTime.UtcNow);
        }

        internal static XactJob ScheduleNextRun(this DbContext dbContext, XactJobPeriodic periodicJob)
        {
            var job = new XactJob(0,
                                  NextRunUtc(periodicJob),
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
