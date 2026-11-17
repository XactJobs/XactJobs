// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace XactJobs
{
    /// <summary>
    /// Code around every job's run, registered with <c>WithJobFilter&lt;T&gt;()</c>: e.g. set up a tenant from the job's arguments, or a
    /// log scope. Resolved from the job's own scope, so it gets the same scoped services as the job, and the same DbContext.
    /// <para>
    /// <c>next</c> runs the next filter, and the last one runs the job method. It all runs inside the job's unit of work: changes made
    /// through the scoped DbContext are saved with the job's, in the transaction that completes it, and its own SaveChanges is checked
    /// as the job's (<c>WithSaveOutsideTransaction</c>). An exception is the job's failure, and is retried as one. A filter can catch the
    /// job's exception, e.g. to throw <see cref="XactJobCancelException"/> instead (no retries), but not swallow it: if the job threw,
    /// the attempt fails, with the job's exception, also when the filter returns (the job's changes, partial, can't be saved as a
    /// completed job's). Not calling <c>next</c> completes the job without running it; calling it twice throws.
    /// </para>
    /// <para>
    /// Filters run only when the job runs: not for a skipped run (a paused periodic job, a continuation whose condition isn't met). The
    /// job's arguments are read before them: a job whose arguments can't be read fails before its filters run.
    /// </para>
    /// </summary>
    public interface IXactJobFilter
    {
        Task InvokeAsync(XactJobExecutingContext context, Func<Task> next);
    }

    /// <summary>The job a filter runs around.</summary>
    public sealed class XactJobExecutingContext
    {
        internal XactJobExecutingContext(XactJob job,
                                         MethodInfo method,
                                         IReadOnlyList<object?> arguments,
                                         IServiceProvider services,
                                         DbContext dbContext,
                                         XactJobContext jobContext,
                                         CancellationToken cancellationToken)
        {
            Job = job;
            Method = method;
            Arguments = arguments;
            Services = services;
            DbContext = dbContext;
            JobContext = jobContext;
            CancellationToken = cancellationToken;
        }

        /// <summary>The job's row: its id, queue, type and method names, scheduled time, batch. Don't change it.</summary>
        public XactJob Job { get; }

        /// <summary>The job method. Its parameters (<c>Method.GetParameters()</c>) are in the order of <see cref="Arguments"/>.</summary>
        public MethodInfo Method { get; }

        /// <summary>
        /// The arguments the job method gets, read from the job: a <see cref="System.Threading.CancellationToken"/> and an
        /// <see cref="XactJobContext"/> parameter get the worker's.
        /// </summary>
        public IReadOnlyList<object?> Arguments { get; }

        /// <summary>The job's scope: the services the job gets.</summary>
        public IServiceProvider Services { get; }

        /// <summary>The job's scoped DbContext: its changes are saved with the job's.</summary>
        public DbContext DbContext { get; }

        /// <summary>The job's <see cref="XactJobContext"/>: its original scheduled time, and a periodic run's state.</summary>
        public XactJobContext JobContext { get; }

        /// <summary>The attempt: 1 for the first, 2 for the first retry.</summary>
        public int Attempt => Job.ErrorCount + 1;

        /// <summary>Cancelled when the worker stops: the job gets the same token.</summary>
        public CancellationToken CancellationToken { get; }
    }
}
