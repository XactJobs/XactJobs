// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

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