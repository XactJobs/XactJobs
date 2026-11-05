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
