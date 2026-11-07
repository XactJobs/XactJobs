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
    public class XactJobBase
    {
        public long Id { get; init; }

        /// <summary>
        /// When should the job be executed
        /// </summary>
        public DateTime ScheduledAt { get; init; }

        /// <summary>
        /// Assembly qualified name of the declaring type
        /// </summary>
        public string TypeName { get; init; }

        /// <summary>
        /// Method name to be called
        /// </summary>
        public string MethodName { get; init; }

        /// <summary>
        /// Arguments to be passed to the method
        /// </summary>
        public string MethodArgs { get; init; }

        public string Queue { get; init; }

        public string? PeriodicJobId { get; init; }
        public string? CronExpression { get; init; }
        public int? PeriodicJobVersion { get; init; }

        public int ErrorCount { get; init; }

        /// <summary>
        /// The time (UTC) the job was originally scheduled for, when it differs from <see cref="ScheduledAt"/>: a retry has it
        /// (also one from the dashboard's history), and so has a run that the dashboard moved to now. It stays the same across retries. Null means the same as ScheduledAt.
        /// A job method gets it as <see cref="XactJobContext.OriginallyScheduledAt"/>.
        /// </summary>
        public DateTime? OriginallyScheduledAt { get; init; }

        private Guid? _batchId;

        /// <summary>
        /// The batch the job belongs to. The continuations of a batch start when all of its jobs are finished.
        /// A retry of the job stays in its batch.
        /// </summary>
        public Guid? BatchId { get => _batchId; init => _batchId = value; }

        /// <summary>
        /// For a continuation: the batch it continues. It's kept by its retries and in the history.
        /// A continuation that waits for its batch is scheduled at <see cref="AwaitingBatchScheduledAt"/>.
        /// </summary>
        public Guid? AwaitsBatchId { get; init; }

        /// <summary>
        /// For a continuation: when it runs, once the batch it waits for is finished.
        /// </summary>
        public XactJobContinuation? ContinuationCondition { get; init; }

        /// <summary>
        /// The scheduled time of a continuation that waits for its batch: the poll for due jobs never reaches it.
        /// When the batch is finished, a worker claims the continuation, and either runs it or skips it.
        /// </summary>
        public static readonly DateTime AwaitingBatchScheduledAt = new(9999, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// True for a continuation that waits for its batch to finish.
        /// </summary>
        public bool IsAwaitingBatch => AwaitsBatchId != null && ScheduledAt == AwaitingBatchScheduledAt;

        /// <summary>
        /// <see cref="OriginallyScheduledAt"/>, or else the scheduled time. A continuation's first attempt waits at the reserved
        /// <see cref="AwaitingBatchScheduledAt"/>, so it gets the time of its first claim instead: the continuation claim saves it,
        /// and <paramref name="claimedAt"/> is that time when the row came back without it (MySQL and Oracle, on the first claim).
        /// </summary>
        internal DateTime GetOriginallyScheduledAt(DateTime claimedAt)
        {
            return OriginallyScheduledAt ?? (IsAwaitingBatch ? claimedAt : ScheduledAt);
        }

        /// <summary>
        /// A job gets its batch when a continuation is added to it, before it's saved.
        /// </summary>
        internal void AssignBatchId(Guid batchId)
        {
            _batchId = batchId;
        }

        public XactJobBase(long id,
                           DateTime scheduledAt,
                           string typeName,
                           string methodName,
                           string methodArgs,
                           string queue,
                           string? periodicJobId = null,
                           string? cronExpression = null,
                           int? periodicJobVersion = null,
                           int errorCount = 0)
        {
            Id = id;
            ScheduledAt = scheduledAt;
            TypeName = typeName;
            MethodName = methodName;
            MethodArgs = methodArgs;
            Queue = queue;
            PeriodicJobId = periodicJobId;
            CronExpression = cronExpression;
            PeriodicJobVersion = periodicJobVersion;
            ErrorCount = errorCount;
        }
    }

} 