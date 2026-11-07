// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using XactJobs.Annotations;
using XactJobs.Internal;

namespace XactJobs
{
    /// <summary>
    /// Jobs whose continuations start when all of them are finished.
    /// Created with <see cref="DbContextExtensions.JobBatch(DbContext)"/>, and its jobs must be saved together, with one SaveChanges:
    /// a batch can't get more jobs once it's saved. Continuations can be added at any time, also later by the batch's id,
    /// with <see cref="DbContextExtensions.JobBatch(DbContext, Guid)"/>.
    /// </summary>
    public sealed class XactJobBatch
    {
        private readonly DbContext _dbContext;
        private readonly bool _isNew;
        private readonly Action<string>? _onJobAdded;
        private readonly List<XactJob> _jobs = [];

        public Guid Id { get; }

        /// <summary>
        /// The jobs added to the batch with this object.
        /// </summary>
        public IReadOnlyList<XactJob> Jobs => _jobs;

        internal XactJobBatch(DbContext dbContext, Guid id, bool isNew, Action<string>? onJobAdded = null)
        {
            _dbContext = dbContext;
            Id = id;
            _isNew = isNew;
            _onJobAdded = onJobAdded;
        }

        public XactJobBatch Enqueue([InstantHandle] Expression<Action> jobExpression, string? queue = null)
        {
            return AddJob(jobExpression, null, queue);
        }

        public XactJobBatch Enqueue<T>([InstantHandle] Expression<Action<T>> jobExpression, string? queue = null)
        {
            return AddJob(jobExpression, null, queue);
        }

        public XactJobBatch Enqueue([InstantHandle] Expression<Func<Task>> jobExpression, string? queue = null)
        {
            return AddJob(jobExpression, null, queue);
        }

        public XactJobBatch Enqueue<T>([InstantHandle] Expression<Func<T, Task>> jobExpression, string? queue = null)
        {
            return AddJob(jobExpression, null, queue);
        }

        public XactJobBatch ScheduleAt([InstantHandle] Expression<Action> jobExpression, DateTime scheduleAt, string? queue = null)
        {
            return AddJob(jobExpression, scheduleAt, queue);
        }

        public XactJobBatch ScheduleAt<T>([InstantHandle] Expression<Action<T>> jobExpression, DateTime scheduleAt, string? queue = null)
        {
            return AddJob(jobExpression, scheduleAt, queue);
        }

        public XactJobBatch ScheduleAt([InstantHandle] Expression<Func<Task>> jobExpression, DateTime scheduleAt, string? queue = null)
        {
            return AddJob(jobExpression, scheduleAt, queue);
        }

        public XactJobBatch ScheduleAt<T>([InstantHandle] Expression<Func<T, Task>> jobExpression, DateTime scheduleAt, string? queue = null)
        {
            return AddJob(jobExpression, scheduleAt, queue);
        }

        public XactJobBatch ScheduleIn([InstantHandle] Expression<Action> jobExpression, TimeSpan delay, string? queue = null)
        {
            return AddJob(jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public XactJobBatch ScheduleIn<T>([InstantHandle] Expression<Action<T>> jobExpression, TimeSpan delay, string? queue = null)
        {
            return AddJob(jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public XactJobBatch ScheduleIn([InstantHandle] Expression<Func<Task>> jobExpression, TimeSpan delay, string? queue = null)
        {
            return AddJob(jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        public XactJobBatch ScheduleIn<T>([InstantHandle] Expression<Func<T, Task>> jobExpression, TimeSpan delay, string? queue = null)
        {
            return AddJob(jobExpression, DateTime.UtcNow.Add(delay), queue);
        }

        /// <summary>
        /// Adds a continuation: a job that starts when all the jobs of the batch are finished, if <paramref name="when"/> is met.
        /// It's saved with SaveChanges. If the batch is already finished, it starts right away.
        /// Returns the continuation, which can have continuations of its own.
        /// </summary>
        public XactJob ContinueWith([InstantHandle] Expression<Action> jobExpression, XactJobContinuation when = XactJobContinuation.OnSuccess, string? queue = null)
        {
            return AddContinuation(jobExpression, when, queue);
        }

        /// <inheritdoc cref="ContinueWith(Expression{Action}, XactJobContinuation, string?)"/>
        public XactJob ContinueWith<T>([InstantHandle] Expression<Action<T>> jobExpression, XactJobContinuation when = XactJobContinuation.OnSuccess, string? queue = null)
        {
            return AddContinuation(jobExpression, when, queue);
        }

        /// <inheritdoc cref="ContinueWith(Expression{Action}, XactJobContinuation, string?)"/>
        public XactJob ContinueWith([InstantHandle] Expression<Func<Task>> jobExpression, XactJobContinuation when = XactJobContinuation.OnSuccess, string? queue = null)
        {
            return AddContinuation(jobExpression, when, queue);
        }

        /// <inheritdoc cref="ContinueWith(Expression{Action}, XactJobContinuation, string?)"/>
        public XactJob ContinueWith<T>([InstantHandle] Expression<Func<T, Task>> jobExpression, XactJobContinuation when = XactJobContinuation.OnSuccess, string? queue = null)
        {
            return AddContinuation(jobExpression, when, queue);
        }

        private XactJobBatch AddJob(LambdaExpression jobExpression, DateTime? scheduledAt, string? queue)
        {
            // the continuations of a saved batch may have started already
            if (!_isNew || _jobs.Any(j => !XactJobContinuations.IsAdded(_dbContext, j)))
            {
                throw new InvalidOperationException($"The batch {Id} is already saved, and can't get more jobs: the jobs of a batch must be saved together.");
            }

            var job = _dbContext.JobAdd(jobExpression, scheduledAt, queue, Id);

            job.IsBatchMember = true;

            _jobs.Add(job);

            _onJobAdded?.Invoke(job.Queue);

            return this;
        }

        private XactJob AddContinuation(LambdaExpression jobExpression, XactJobContinuation when, string? queue)
        {
            if (_isNew && _jobs.Count == 0)
            {
                throw new InvalidOperationException("Add the jobs of the batch before its continuations: a batch without jobs is finished, so its continuations would start right away.");
            }

            return XactJobContinuations.Add(_dbContext, Id, jobExpression, when, queue);
        }
    }
}
