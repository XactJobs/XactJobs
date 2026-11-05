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
    public class XactJob: XactJobBase
    {
        public DateTime? LeasedUntil { get; init; }
        public Guid? Leaser { get; init; }

        // Not mapped: the context the job was added to by the API, so that continuations can be added to it before it's saved.
        internal DbContext? AddedTo { get; set; }

        // Not mapped: added to a batch with JobBatch(), as opposed to getting its own batch when a continuation was added to it.
        internal bool IsBatchMember { get; set; }

        public XactJob(long id,
                       DateTime scheduledAt,
                       string typeName,
                       string methodName,
                       string methodArgs,
                       string queue,
                       string? periodicJobId = null,
                       string? cronExpression = null,
                       int? periodicJobVersion = null,
                       int errorCount = 0,
                       Guid? leaser = null,
                       DateTime? leasedUntil = null)
            : base(id,
                   scheduledAt,
                   typeName,
                   methodName,
                   methodArgs,
                   queue,
                   periodicJobId,
                   cronExpression,
                   periodicJobVersion,
                   errorCount)
        {
            Leaser = leaser;
            LeasedUntil = leasedUntil;
        }

        /// <summary>
        /// Adds a continuation: a job that starts when this job is finished, if <paramref name="when"/> is met.
        /// It's saved with SaveChanges, together with this job, which can't be saved yet: its row can't be changed later.
        /// Returns the continuation, which can have continuations of its own.
        /// </summary>
        public XactJob ContinueWith([InstantHandle] Expression<Action> jobExpression, XactJobContinuation when = XactJobContinuation.OnSuccess, string? queue = null)
        {
            return XactJobContinuations.ContinueJob(this, jobExpression, when, queue);
        }

        /// <inheritdoc cref="ContinueWith(Expression{Action}, XactJobContinuation, string?)"/>
        public XactJob ContinueWith<T>([InstantHandle] Expression<Action<T>> jobExpression, XactJobContinuation when = XactJobContinuation.OnSuccess, string? queue = null)
        {
            return XactJobContinuations.ContinueJob(this, jobExpression, when, queue);
        }

        /// <inheritdoc cref="ContinueWith(Expression{Action}, XactJobContinuation, string?)"/>
        public XactJob ContinueWith([InstantHandle] Expression<Func<Task>> jobExpression, XactJobContinuation when = XactJobContinuation.OnSuccess, string? queue = null)
        {
            return XactJobContinuations.ContinueJob(this, jobExpression, when, queue);
        }

        /// <inheritdoc cref="ContinueWith(Expression{Action}, XactJobContinuation, string?)"/>
        public XactJob ContinueWith<T>([InstantHandle] Expression<Func<T, Task>> jobExpression, XactJobContinuation when = XactJobContinuation.OnSuccess, string? queue = null)
        {
            return XactJobContinuations.ContinueJob(this, jobExpression, when, queue);
        }
    }
} 