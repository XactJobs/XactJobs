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
        internal XactJobContext(DateTime originallyScheduledAt)
        {
            OriginallyScheduledAt = DateTime.SpecifyKind(originallyScheduledAt, DateTimeKind.Utc);
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
    }
}
