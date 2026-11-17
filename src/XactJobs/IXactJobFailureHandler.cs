// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

namespace XactJobs
{
    /// <summary>
    /// Told when a job has failed for good, registered with <c>WithJobFailureHandler&lt;T&gt;()</c>: e.g. to send an alert. Called
    /// once per job, when it's out of retries or threw <see cref="XactJobCancelException"/>, not for the attempts that are retried.
    /// <para>
    /// Called after the failure is saved, and after the job's place on the worker is freed, in a scope of its own: it can't change the
    /// job's outcome. Its exceptions are logged, and the next handler is still called. The handlers of a job get 30 seconds in all,
    /// then their token is cancelled; during the shutdown, the app may stop before they finish. It isn't called if the failure couldn't
    /// be saved (the job then runs again when its lease expires).
    /// </para>
    /// </summary>
    public interface IXactJobFailureHandler
    {
        Task OnFailedAsync(XactJobFailedContext context, CancellationToken cancellationToken);
    }

    /// <summary>The job that failed for good.</summary>
    public sealed class XactJobFailedContext
    {
        internal XactJobFailedContext(XactJob job, Exception exception, IServiceProvider services)
        {
            Job = job;
            Exception = exception;
            Services = services;
        }

        /// <summary>
        /// The job as its last attempt was claimed: its id, queue, type and method names. A snapshot: its row is gone (it's in the history
        /// now), so don't attach it to a DbContext.
        /// </summary>
        public XactJob Job { get; }

        /// <summary>What the last attempt threw.</summary>
        public Exception Exception { get; }

        /// <summary>The last attempt: 1 if the first one failed for good.</summary>
        public int Attempt => Job.ErrorCount + 1;

        /// <summary>True if the job threw <see cref="XactJobCancelException"/>, false if it's out of retries.</summary>
        public bool IsCancelledByJob => XactJobCancelException.Find(Exception) != null;

        /// <summary>The handlers' scope.</summary>
        public IServiceProvider Services { get; }
    }
}
