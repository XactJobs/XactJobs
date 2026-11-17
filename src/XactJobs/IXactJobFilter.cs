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
    /// completed job's). Not calling <c>next</c> completes the job without running it; calling it twice throws. To record the run as
    /// skipped instead, with a reason, call <see cref="XactJobExecutingContext.Skip"/> and don't call <c>next</c>.
    /// </para>
    /// <para>
    /// Filters run only when the worker runs the job: not for a run it skips itself (a paused periodic job, a continuation whose condition
    /// isn't met). The
    /// job's arguments are read before them: a job whose arguments can't be read fails before its filters run.
    /// </para>
    /// </summary>
    public interface IXactJobFilter
    {
        Task InvokeAsync(XactJobExecutingContext context, Func<Task> next);
    }

    /// <summary>A filter's misuse of <c>next</c> or <c>Skip</c>: a bug in the filter, which a retry wouldn't fix.</summary>
    internal sealed class XactJobFilterMisuseException(string message) : XactJobCancelException(message);

    /// <summary>The job a filter runs around.</summary>
    public sealed class XactJobExecutingContext
    {
        private readonly string? _timeZoneId;
        private TimeZoneInfo? _timeZone;

        internal XactJobExecutingContext(XactJob job,
                                         MethodInfo method,
                                         IReadOnlyList<object?> arguments,
                                         IServiceProvider services,
                                         DbContext dbContext,
                                         XactJobContext jobContext,
                                         string? timeZoneId,
                                         CancellationToken cancellationToken)
        {
            _timeZoneId = timeZoneId;
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

        /// <summary>
        /// The time zone of a periodic job's schedule, e.g. to tell whether the run is on a holiday there:
        /// <c>TimeZoneInfo.ConvertTimeFromUtc(context.JobContext.OriginallyScheduledAt, context.TimeZone)</c>. UTC for other jobs.
        /// Throws <see cref="TimeZoneNotFoundException"/> if this node doesn't know the zone, which fails the attempt: a node that runs a
        /// periodic job in a time zone needs its tzdata.
        /// </summary>
        public TimeZoneInfo TimeZone => _timeZone ??= CronUtil.CronTimeZones.Find(_timeZoneId);

        /// <summary>The reason given to <see cref="Skip"/>, or null if the run isn't skipped.</summary>
        public string? SkipReason { get; private set; }

        /// <summary>Set by the filters' chain when it calls the job method.</summary>
        internal bool IsJobInvoked { get; set; }

        /// <summary>The first misuse of <c>next</c> or <see cref="Skip"/>: the attempt fails with it, also if a filter catches it.</summary>
        internal XactJobFilterMisuseException? Misuse { get; private set; }

        /// <summary>Set by the filters' chain when it's done: a <see cref="Skip"/> after it would be lost.</summary>
        internal bool IsChainFinished { get; set; }

        internal XactJobFilterMisuseException SetMisuse(string message)
        {
            var exception = new XactJobFilterMisuseException(message);

            Misuse ??= exception;

            return exception;
        }

        /// <summary>
        /// Skips the run: the job method doesn't run, and the run is recorded in the history as skipped, with the reason. A periodic
        /// job's next run is scheduled as usual, and its state is kept. Changes made through the DbContext are saved with the record, as
        /// for a completed job, but a new periodic state (<see cref="XactJobContext.SetPeriodicState"/>) isn't: setting one fails the
        /// attempt. Don't call <c>next</c> then: it throws. A misuse (<c>next</c> after <c>Skip</c>, <c>next</c> twice, <c>Skip</c> after
        /// the job ran, a state set in a skipped run) fails the attempt without retries, also if a filter catches the exception, and with
        /// the job's own exception if the job threw. In a batch, a skipped job counts as not succeeded for its continuations, like a
        /// skipped continuation.
        /// </summary>
        /// <exception cref="XactJobCancelException">The job already ran: a misuse, which fails the attempt without retries.</exception>
        /// <exception cref="InvalidOperationException">The run is over: the filter returned before calling it.</exception>
        public void Skip(string reason)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(reason);

            if (IsJobInvoked) throw SetMisuse("The job already ran: a run can be skipped only before the job runs.");

            if (IsChainFinished) throw new InvalidOperationException("The run is over: Skip() is called from a filter, before it returns.");

            SkipReason = reason;
        }
    }
}
