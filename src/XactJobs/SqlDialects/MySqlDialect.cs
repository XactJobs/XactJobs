using Microsoft.EntityFrameworkCore;
using UUIDNext;

namespace XactJobs.SqlDialects
{
    public class MySqlDialect : ISqlDialect
    {
        public bool HasSchemaSupport { get; } = false;

        public string XactJobSchema { get; } = "xact_jobs";
        public string XactJobTable { get; } = "job";
        public string XactJobArchiveTable { get; } = "job_archive";
        public string XactJobPeriodicTable { get; } = "job_periodic";

        public string ColId { get; } = "id";
        public string ColCreatedAt { get; } = "created_at";
        public string ColScheduledAt { get; } = "scheduled_at";
        public string ColCompletedAt { get; } = "completed_at";
        public string ColLeasedUntil { get; } = "leased_until";
        public string ColLeaser { get; } = "leaser";
        public string ColTypeName { get; } = "type_name";
        public string ColMethodName { get; } = "method_name";
        public string ColMethodArgs { get; } = "method_args";
        public string ColStatus { get; } = "status";
        public string ColQueue { get; } = "queue";
        public string ColPeriodicJobId { get; } = "periodic_job_id";
        public string ColPeriodicJobName { get; } = "periodic_job_name";
        public string ColErrorCount { get; } = "error_count";
        public string ColErrorTime { get; } = "error_time";
        public string ColErrorMessage { get; } = "error_message";
        public string ColErrorStackTrace { get; } = "error_stack_trace";
        public string ColCronExpression { get; } = "cron_expression";
        public string ColName { get; } = "name";
        public string ColUpdatedAt { get; } = "updated_at";
        public string ColIsActive { get; } = "is_active";

        public string DateTimeColumnType { get; } = "datetime(6)";

        public Guid NewJobId() => Uuid.NewDatabaseFriendly(Database.Other);

        public string? GetAcquireLeaseSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE {XactJobSchema}_{XactJobTable} AS t
JOIN (
    SELECT {ColId}
    FROM {XactJobSchema}_{XactJobTable}
    WHERE {ColStatus} IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
      AND {ColScheduledAt} <= UTC_TIMESTAMP 
      AND {ColQueue} = '{queueName ?? QueueNames.Default}'
      AND ({ColLeasedUntil} IS NULL OR {ColLeasedUntil} < UTC_TIMESTAMP)
    ORDER BY {ColScheduledAt}
    LIMIT {maxJobs}
    FOR UPDATE SKIP LOCKED
) AS sub ON t.{ColId} = sub.{ColId}
SET t.{ColLeaser} = '{leaser}',
    t.{ColLeasedUntil} = UTC_TIMESTAMP + INTERVAL {leaseDurationInSeconds} SECOND
";

        public string GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
SELECT *
FROM {XactJobSchema}_{XactJobTable}
WHERE {ColLeaser} = '{leaser}'
  AND {ColLeasedUntil} >= UTC_TIMESTAMP
  AND {ColQueue} = '{queueName ?? QueueNames.Default}'
LIMIT {maxJobs}
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeasedUntil} = UTC_TIMESTAMP + INTERVAL {leaseDurationInSeconds} SECOND
WHERE {ColLeaser} = '{leaser}'
  AND {ColStatus} IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";

        public string GetClearLeaseSql(Guid leaser) => $@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeaser} = NULL, {ColLeasedUntil} = NULL
WHERE {ColLeaser} = '{leaser}'
  AND {ColStatus} IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
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
