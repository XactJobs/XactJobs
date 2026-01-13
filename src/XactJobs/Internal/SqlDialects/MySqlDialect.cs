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

        public string DateTimeColumnType { get; } = "datetime(6)";

        public string? GetAcquireLeaseSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE {XactJobSchema}_{XactJobTable} AS t
JOIN (
    SELECT {ColId}, {ColQueue}, {ColScheduledAt}
    FROM {XactJobSchema}_{XactJobTable}
    WHERE {ColScheduledAt} <= UTC_TIMESTAMP 
      AND {ColQueue} = '{queueName ?? QueueNames.Default}'
      AND ({ColLeasedUntil} IS NULL OR {ColLeasedUntil} < UTC_TIMESTAMP)
    ORDER BY {ColScheduledAt}
    LIMIT {maxJobs}
    FOR UPDATE SKIP LOCKED
) AS sub ON -- join on the entire PK
    t.{ColId} = sub.{ColId}
    AND t.{ColQueue} = sub.{ColQueue}
    AND t.{ColScheduledAt} = sub.{ColScheduledAt}
SET t.{ColLeaser} = '{leaser}',
    t.{ColLeasedUntil} = UTC_TIMESTAMP + INTERVAL {leaseDurationInSeconds} SECOND
";

        public string GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
SELECT *
FROM {XactJobSchema}_{XactJobTable}
WHERE {ColLeaser} = '{leaser}'
  AND {ColLeasedUntil} >= UTC_TIMESTAMP
LIMIT {maxJobs}
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeasedUntil} = UTC_TIMESTAMP + INTERVAL {leaseDurationInSeconds} SECOND
WHERE {ColLeaser} = '{leaser}'
";

        public string GetClearLeaseSql(Guid leaser) => $@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeaser} = NULL, {ColLeasedUntil} = NULL
WHERE {ColLeaser} = '{leaser}'
";

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
