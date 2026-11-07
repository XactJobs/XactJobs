// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace XactJobs.Internal
{
    internal static class XactJobContinuations
    {
        /// <summary>
        /// A job continued on its own becomes a batch of one.
        /// </summary>
        internal static XactJob ContinueJob(XactJob parent, LambdaExpression jobExpression, XactJobContinuation when, string? queue)
        {
            var dbContext = parent.AddedTo;

            if (dbContext == null || !IsAdded(dbContext, parent))
            {
                throw new InvalidOperationException(
                    "Continuations can only be added to a job that was just added with JobEnqueue, JobScheduleAt or JobScheduleIn, and isn't saved yet: " +
                    "a saved job can't be changed. To continue jobs that are saved separately, add them to a batch with JobBatch(), " +
                    "and continue the batch, later by its id with JobBatch(batchId).");
            }

            if (parent.PeriodicJobId != null)
            {
                throw new NotSupportedException("A run of a periodic job can't have continuations.");
            }

            if (parent.IsBatchMember)
            {
                throw new InvalidOperationException($"The job is in the batch {parent.BatchId}: continue the batch instead.");
            }

            if (parent.BatchId == null)
            {
                parent.AssignBatchId(Guid.NewGuid());
            }

            return Add(dbContext, parent.BatchId!.Value, jobExpression, when, queue);
        }

        /// <summary>
        /// False if the job is saved, or its context is disposed.
        /// </summary>
        internal static bool IsAdded(DbContext dbContext, XactJob job)
        {
            try
            {
                return dbContext.Entry(job).State == EntityState.Added;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
        }

        internal static XactJob Add(DbContext dbContext, Guid batchId, LambdaExpression jobExpression, XactJobContinuation when, string? queue)
        {
            if (!Enum.IsDefined(when))
            {
                throw new ArgumentOutOfRangeException(nameof(when), when, "Unknown continuation condition.");
            }

            var job = XactJobSerializer.FromExpression(jobExpression, XactJobBase.AwaitingBatchScheduledAt, queue, awaitsBatchId: batchId, continuationCondition: when);

            dbContext.Set<XactJob>().Add(job);

            job.AddedTo = dbContext;

            return job;
        }
    }
}
