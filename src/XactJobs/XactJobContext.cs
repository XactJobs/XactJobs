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
        /// <item>A continuation: the time its first attempt was claimed, once its batch was finished.</item>
        /// </list>
        /// </summary>
        public DateTime OriginallyScheduledAt { get; }
    }
}
