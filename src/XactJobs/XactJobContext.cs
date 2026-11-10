// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

namespace XactJobs
{
    /// <summary>
    /// About the run of a job. A job method gets it by taking a parameter of this type, and passing null for it when the job
    /// is enqueued, e.g. <c>x.SendReportAsync(null)</c>: the worker fills it in when the job runs, as it does for a CancellationToken.
    /// A value passed when the job is enqueued isn't stored.
    /// </summary>
    public sealed class XactJobContext
    {
        private string? _periodicState;

        internal XactJobContext(DateTime originallyScheduledAt) : this(originallyScheduledAt, null)
        {
        }

        internal XactJobContext(DateTime originallyScheduledAt, XactJobPeriodic? periodicJob)
        {
            OriginallyScheduledAt = DateTime.SpecifyKind(originallyScheduledAt, DateTimeKind.Utc);
            PeriodicJobId = periodicJob?.Id;
            _periodicState = periodicJob?.State;
        }

        /// <summary>
        /// The time (UTC) the job was originally scheduled for, which stays the same across its retries, e.g. to build
        /// an idempotency key: <c>$"daily-report-{context.OriginallyScheduledAt:yyyy-MM-dd}"</c>.
        /// <list type="bullet">
        /// <item>A job: the time it was scheduled for, also when it runs late, when the dashboard's Run now moved it to now,
        /// or when it's retried from the dashboard's history.</item>
        /// <item>A run of a periodic job: its cron slot.</item>
        /// <item>A continuation: the time its first attempt was claimed, once its batch was finished. The claim saves it,
        /// so a second run of that attempt, after its lease expired, gets the same time.</item>
        /// </list>
        /// </summary>
        public DateTime OriginallyScheduledAt { get; }

        /// <summary>
        /// The id of the periodic job, if this is one of its runs, or null.
        /// </summary>
        public string? PeriodicJobId { get; }

        /// <summary>
        /// A run of a periodic job: the state the last completed run left for this one, or null if there's none (the first run, or
        /// no run set one). After <see cref="SetPeriodicState"/>, the state this run set.
        /// </summary>
        public string? PeriodicState => _periodicState;

        /// <summary>
        /// Whether this run called <see cref="SetPeriodicState"/>.
        /// </summary>
        internal bool IsPeriodicStateSet { get; private set; }

        /// <summary>
        /// Sets the state the next run of the periodic job gets in <see cref="PeriodicState"/>: e.g. a watermark, or a cursor. It's
        /// saved only if the run completes, in the same transaction as the job's own changes: a failed attempt leaves the
        /// state as it was, and its retry gets the same one. A run that doesn't call this keeps the state as it is; null clears it.
        /// The history keeps each run's state, before and after.
        /// </summary>
        /// <exception cref="InvalidOperationException">The job isn't a run of a periodic job.</exception>
        /// <exception cref="ArgumentException">The state is longer than <see cref="XactJobPeriodic.MaxStateLength"/> characters.</exception>
        public void SetPeriodicState(string? state)
        {
            if (PeriodicJobId == null)
            {
                throw new InvalidOperationException("Only a run of a periodic job has a state.");
            }

            if (state?.Length > XactJobPeriodic.MaxStateLength)
            {
                throw new ArgumentException($"The state of a periodic job can't be longer than {XactJobPeriodic.MaxStateLength} characters, and this one has {state.Length}.", nameof(state));
            }

            _periodicState = state;
            IsPeriodicStateSet = true;
        }
    }
}
