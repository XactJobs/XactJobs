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
    public class PostgreSqlDialect : ISqlDialect
    {
        /// <param name="schema">The schema of XactJobs' tables, xact_jobs by default. Lowercase: the SQL doesn't quote it.</param>
        public PostgreSqlDialect(string? schema = null)
        {
            XactJobSchema = SqlDialectExtensions.CheckSchemaName(schema, "xact_jobs", @"^[a-z_][a-z0-9_]{0,62}$");
        }

        public string XactJobSchema { get; }
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

        public bool HasSchemaSupport { get; } = true;

        public string DateTimeColumnType { get; } = "timestamptz";

        public IsolationLevel? ClaimIsolationLevel { get; } = null;

        public Task<List<XactJob>> ClaimJobsAsync(DbContext db, string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds, CancellationToken cancellationToken)
        {
            return SqlDialectExtensions.ClaimJobsInOneStatementAsync(db, GetFetchJobsSql(queueName, maxJobs, leaser, leaseDurationInSeconds), cancellationToken);
        }

        public (string Sql, object[] Parameters) GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => ($@"
WITH cte AS (
  SELECT {ColId}, {ColQueue}, {ColScheduledAt}
  FROM {XactJobSchema}.{XactJobTable}
  WHERE {ColScheduledAt} <= current_timestamp
    AND {ColQueue} = {{0}}
    AND ({ColLeasedUntil} IS NULL OR {ColLeasedUntil} < current_timestamp)
  ORDER BY {ColScheduledAt}
  FOR UPDATE SKIP LOCKED
  LIMIT {{1}}
)
UPDATE {XactJobSchema}.{XactJobTable}
SET {ColLeaser} = {{2}}, {ColLeasedUntil} = current_timestamp + make_interval(secs => {{3}}::double precision)
FROM cte
WHERE -- we need to join on all these, since these are the PK
    {XactJobSchema}.{XactJobTable}.{ColId} = cte.{ColId}
    AND {XactJobSchema}.{XactJobTable}.{ColQueue} = cte.{ColQueue}
    AND {XactJobSchema}.{XactJobTable}.{ColScheduledAt} = cte.{ColScheduledAt}
RETURNING {XactJobSchema}.{XactJobTable}.*
", [queueName ?? QueueNames.Default, maxJobs, leaser, leaseDurationInSeconds]);

        public Task<List<XactJob>> ClaimContinuationsAsync(DbContext db, string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds, DateTime claimedAt, CancellationToken cancellationToken)
        {
            return SqlDialectExtensions.ClaimJobsInOneStatementAsync(db, GetFetchContinuationsSql(queueName, maxJobs, leaser, leaseDurationInSeconds, claimedAt), cancellationToken);
        }

        // The batch's jobs are read without locking them (FOR UPDATE OF c), so a job that's being retried can't be skipped.
        // The original time isn't indexed, so setting it keeps the claim a HOT update.
        public (string Sql, object[] Parameters) GetFetchContinuationsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds, DateTime claimedAt) => ($@"
WITH cte AS (
  SELECT c.{ColId}, c.{ColQueue}, c.{ColScheduledAt}
  FROM {XactJobSchema}.{XactJobTable} AS c
  WHERE c.{ColQueue} = {{0}}
    AND c.{ColScheduledAt} = {{1}}
    AND c.{ColAwaitsBatchId} IS NOT NULL
    AND (c.{ColLeasedUntil} IS NULL OR c.{ColLeasedUntil} < current_timestamp)
    AND NOT EXISTS (SELECT 1 FROM {XactJobSchema}.{XactJobTable} AS b WHERE b.{ColBatchId} = c.{ColAwaitsBatchId})
  ORDER BY c.{ColId}
  FOR UPDATE OF c SKIP LOCKED
  LIMIT {{2}}
)
UPDATE {XactJobSchema}.{XactJobTable}
SET {ColLeaser} = {{3}}, {ColLeasedUntil} = current_timestamp + make_interval(secs => {{4}}::double precision),
    {ColOriginallyScheduledAt} = COALESCE({XactJobSchema}.{XactJobTable}.{ColOriginallyScheduledAt}, {{5}})
FROM cte
WHERE
    {XactJobSchema}.{XactJobTable}.{ColId} = cte.{ColId}
    AND {XactJobSchema}.{XactJobTable}.{ColQueue} = cte.{ColQueue}
    AND {XactJobSchema}.{XactJobTable}.{ColScheduledAt} = cte.{ColScheduledAt}
RETURNING {XactJobSchema}.{XactJobTable}.*
", [queueName ?? QueueNames.Default, XactJobBase.AwaitingBatchScheduledAt, maxJobs, leaser, leaseDurationInSeconds, claimedAt]);

        public (string Sql, object[] Parameters) GetExtendLeaseSql(Guid leaser, IReadOnlyCollection<XactJob> jobs, int leaseDurationInSeconds) => ($@"
UPDATE {XactJobSchema}.{XactJobTable} AS j
SET {ColLeasedUntil} = current_timestamp + make_interval(secs => {{0}}::double precision)
FROM unnest({{1}}::text[], {{2}}::timestamptz[], {{3}}::bigint[]) AS k(queue, scheduled_at, id)
WHERE j.{ColQueue} = k.queue
  AND j.{ColScheduledAt} = k.scheduled_at
  AND j.{ColId} = k.id
  AND j.{ColLeaser} = {{4}}
", [
            leaseDurationInSeconds,
            jobs.Select(x => x.Queue).ToArray(),
            jobs.Select(x => x.ScheduledAt).ToArray(),
            jobs.Select(x => x.Id).ToArray(),
            leaser
        ]);

        public (string Sql, object[] Parameters) GetClearLeaseSql(Guid leaser, IReadOnlyCollection<XactJob> jobs) => ($@"
UPDATE {XactJobSchema}.{XactJobTable} AS j
SET {ColLeaser} = NULL, {ColLeasedUntil} = NULL
FROM unnest({{0}}::text[], {{1}}::timestamptz[], {{2}}::bigint[]) AS k(queue, scheduled_at, id)
WHERE j.{ColQueue} = k.queue
  AND j.{ColScheduledAt} = k.scheduled_at
  AND j.{ColId} = k.id
  AND j.{ColLeaser} = {{3}}
", [
            jobs.Select(x => x.Queue).ToArray(),
            jobs.Select(x => x.ScheduledAt).ToArray(),
            jobs.Select(x => x.Id).ToArray(),
            leaser
        ]);

        public async Task AcquireTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            var sql = @$"LOCK TABLE {XactJobSchema}.{XactJobPeriodicTable} IN EXCLUSIVE MODE";

            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task ReleaseTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
