using Microsoft.EntityFrameworkCore;

namespace XactJobs.Internal.SqlDialects
{
    public class SqliteDialect : ISqlDialect
    {
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

        public bool HasSchemaSupport { get; } = false;

        public string DateTimeColumnType { get; } = "DATETIME"; // SQLite stores datetimes as ISO8601 text

        public string? GetAcquireLeaseSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => null;

        public string GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
WITH cte AS (
  SELECT {ColId}, {ColQueue}, {ColScheduledAt}
  FROM {XactJobSchema}_{XactJobTable}
  WHERE {ColScheduledAt} <= datetime('subsec')
    AND {ColQueue} = '{queueName ?? QueueNames.Default}'
    AND ({ColLeasedUntil} IS NULL OR {ColLeasedUntil} < datetime('subsec'))
  ORDER BY {ColScheduledAt}
  LIMIT {maxJobs}
)
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeaser} = '{leaser}', 
    {ColLeasedUntil} = datetime('now', '+{leaseDurationInSeconds} seconds')
FROM cte
WHERE
    {XactJobSchema}_{XactJobTable}.{ColId} = cte.{ColId}
    AND {XactJobSchema}_{XactJobTable}.{ColQueue} = cte.{ColQueue}
    AND {XactJobSchema}_{XactJobTable}.{ColScheduledAt} = cte.{ColScheduledAt}
RETURNING *
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeasedUntil} = datetime('now', '+{leaseDurationInSeconds} seconds')
WHERE {ColLeaser} = '{leaser}'
";

        public string GetClearLeaseSql(Guid leaser) => $@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeaser} = NULL, {ColLeasedUntil} = NULL
WHERE {ColLeaser} = '{leaser}'
";

        public Task AcquireTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ReleaseTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

