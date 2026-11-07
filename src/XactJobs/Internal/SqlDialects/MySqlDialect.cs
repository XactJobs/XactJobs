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

using Microsoft.EntityFrameworkCore;
using System.Data;

namespace XactJobs.Internal.SqlDialects
{
    public class MySqlDialect : ISqlDialect
    {
        public bool HasSchemaSupport { get; } = false;

        public string XactJobSchema { get; } = "xact_jobs";
        public string XactJobTable { get; } = "job";
        public string XactJobHistoryTable { get; } = "job_history";
        public string XactJobPeriodicTable { get; } = "job_periodic";

        public string PrimaryKeyPrefix { get; } = "pk";
        public string IndexPrefix { get; } = "ix";

        public string ColId { get; } = "id";
        public string ColCreatedAt { get; } = "created_at";
        public string ColScheduledAt { get; } = "scheduled_at";
        public string ColProcessedAt { get; } = "processed_at";
        public string ColLeasedUntil { get; } = "leased_until";
        public string ColLeaser { get; } = "leaser";
        public string ColTypeName { get; } = "type_name";
        public string ColMethodName { get; } = "method_name";
        public string ColMethodArgs { get; } = "method_args";
        public string ColStatus { get; } = "status";
        public string ColQueue { get; } = "queue";
        public string ColPeriodicJobId { get; } = "periodic_job_id";
        public string ColPeriodicJobVersion { get; } = "periodic_job_version";
        public string ColErrorCount { get; } = "error_count";
        public string ColErrorTime { get; } = "error_time";
        public string ColErrorMessage { get; } = "error_message";
        public string ColErrorStackTrace { get; } = "error_stack_trace";
        public string ColCronExpression { get; } = "cron_expression";
        public string ColUpdatedAt { get; } = "updated_at";
        public string ColIsActive { get; } = "is_active";
        public string ColVersion { get; } = "version";
        public string ColBatchId { get; } = "batch_id";
        public string ColAwaitsBatchId { get; } = "awaits_batch_id";
        public string ColContinuationCondition { get; } = "continuation_condition";
        public string ColOriginallyScheduledAt { get; } = "originally_scheduled_at";

        public string DateTimeColumnType { get; } = "datetime(6)";

        // InnoDB's default, REPEATABLE READ, would make the locking select of the claim lock every row it scans, leased (running) jobs
        // too, and the gaps between them: a running job couldn't be deleted, nor a new job inserted, until the claim commits.
        // READ COMMITTED releases the rows that don't match at once, and doesn't lock gaps.
        // MySqlConnector (Pomelo) sets the level for the session, not only for the transaction. That's fine: the claim's scope does
        // nothing else, and the pool's connection reset, or the next transaction (MySqlConnector sets its level too), sets it back.
        public IsolationLevel? ClaimIsolationLevel { get; } = IsolationLevel.ReadCommitted;

        public Task<List<XactJob>> ClaimJobsAsync(DbContext db, string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds, CancellationToken cancellationToken)
        {
            return SqlDialectExtensions.ClaimJobsWithLockAsync(db,
                                                               ClaimIsolationLevel,
                                                               GetLockJobsSql(queueName, maxJobs),
                                                               jobs => GetLeaseJobsSql(leaser, jobs, leaseDurationInSeconds),
                                                               cancellationToken);
        }

        // UTC_TIMESTAMP(6) keeps the fraction of a second: with plain UTC_TIMESTAMP a lease can be up to 1s too short
        public (string Sql, object[] Parameters) GetLockJobsSql(string? queueName, int maxJobs) => ($@"
SELECT *
FROM {XactJobSchema}_{XactJobTable}
WHERE {ColScheduledAt} <= UTC_TIMESTAMP(6)
  AND {ColQueue} = {{0}}
  AND ({ColLeasedUntil} IS NULL OR {ColLeasedUntil} < UTC_TIMESTAMP(6))
ORDER BY {ColScheduledAt}
LIMIT {{1}}
FOR UPDATE SKIP LOCKED
", [queueName ?? QueueNames.Default, maxJobs]);

        // The leaser is passed as a string, because the column is char(36).
        // For continuations, claimedAt sets their original time, if it isn't set yet (see ISqlDialect.ClaimContinuationsAsync).
        public (string Sql, object[] Parameters) GetLeaseJobsSql(Guid leaser, IReadOnlyCollection<XactJob> jobs, int leaseDurationInSeconds, DateTime? claimedAt = null)
        {
            List<object> parameters = [leaser.ToString(), leaseDurationInSeconds];

            var setOriginallyScheduledAt = "";

            if (claimedAt.HasValue)
            {
                setOriginallyScheduledAt = $@",
    {ColOriginallyScheduledAt} = COALESCE({ColOriginallyScheduledAt}, {{{parameters.Count}}})";

                parameters.Add(claimedAt.Value);
            }

            var jobKeys = SqlDialectExtensions.BuildJobKeysPredicate(jobs, parameters, ColQueue, ColScheduledAt, ColId);

            return ($@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeaser} = {{0}},
    {ColLeasedUntil} = UTC_TIMESTAMP(6) + INTERVAL {{1}} SECOND{setOriginallyScheduledAt}
WHERE {jobKeys}
", parameters.ToArray());
        }

        public Task<List<XactJob>> ClaimContinuationsAsync(DbContext db, string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds, DateTime claimedAt, CancellationToken cancellationToken)
        {
            return SqlDialectExtensions.ClaimJobsWithLockAsync(db,
                                                               ClaimIsolationLevel,
                                                               GetLockContinuationsSql(queueName, maxJobs),
                                                               jobs => GetLeaseJobsSql(leaser, jobs, leaseDurationInSeconds, claimedAt),
                                                               cancellationToken);
        }

        // only the continuations are locked (FOR UPDATE OF c): the batch's jobs are a consistent read, which doesn't skip rows
        public (string Sql, object[] Parameters) GetLockContinuationsSql(string? queueName, int maxJobs) => ($@"
SELECT c.*
FROM {XactJobSchema}_{XactJobTable} AS c
WHERE c.{ColQueue} = {{0}}
  AND c.{ColScheduledAt} = {{1}}
  AND c.{ColAwaitsBatchId} IS NOT NULL
  AND (c.{ColLeasedUntil} IS NULL OR c.{ColLeasedUntil} < UTC_TIMESTAMP(6))
  AND NOT EXISTS (SELECT 1 FROM {XactJobSchema}_{XactJobTable} AS b WHERE b.{ColBatchId} = c.{ColAwaitsBatchId})
ORDER BY c.{ColId}
LIMIT {{2}}
FOR UPDATE OF c SKIP LOCKED
", [queueName ?? QueueNames.Default, XactJobBase.AwaitingBatchScheduledAt, maxJobs]);

        public (string Sql, object[] Parameters) GetExtendLeaseSql(Guid leaser, IReadOnlyCollection<XactJob> jobs, int leaseDurationInSeconds)
        {
            List<object> parameters = [leaseDurationInSeconds, leaser.ToString()];

            var jobKeys = SqlDialectExtensions.BuildJobKeysPredicate(jobs, parameters, ColQueue, ColScheduledAt, ColId);

            return ($@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeasedUntil} = UTC_TIMESTAMP(6) + INTERVAL {{0}} SECOND
WHERE {ColLeaser} = {{1}}
  AND ({jobKeys})
", parameters.ToArray());
        }

        public (string Sql, object[] Parameters) GetClearLeaseSql(Guid leaser, IReadOnlyCollection<XactJob> jobs)
        {
            List<object> parameters = [leaser.ToString()];

            var jobKeys = SqlDialectExtensions.BuildJobKeysPredicate(jobs, parameters, ColQueue, ColScheduledAt, ColId);

            return ($@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeaser} = NULL, {ColLeasedUntil} = NULL
WHERE {ColLeaser} = {{0}}
  AND ({jobKeys})
", parameters.ToArray());
        }

        public async Task AcquireTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            var tableLockName = $"{tableSchema}_{tableName}";

            var result = await db.Database.ExecuteScalarIntAsync($"SELECT GET_LOCK('{tableLockName}', 30)", cancellationToken)
                .ConfigureAwait(false);

            if (result != 1) throw new Exception($"Failed to acquire lock (LockName='{tableLockName}', Result={result})");
        }

        public async Task ReleaseTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            var tableLockName = $"{tableSchema}_{tableName}";

            var result = await db.Database.ExecuteScalarIntAsync($"SELECT RELEASE_LOCK('{tableLockName}')", cancellationToken)
                .ConfigureAwait(false);

            if (result != 1) throw new Exception($"Failed to release lock (LockName='{tableLockName}', Result={result})");
        }
    }
}
