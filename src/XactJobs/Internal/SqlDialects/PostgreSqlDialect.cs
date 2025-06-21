using Microsoft.EntityFrameworkCore;

namespace XactJobs.Internal.SqlDialects
{
    public class PostgreSqlDialect : ISqlDialect
    {
        public string XactJobSchema { get; } = "xact_jobs";
        public string XactJobTable { get; } = "job";
        public string XactJobHistoryTable { get; } = "job_history";
        public string XactJobPeriodicTable { get; } = "job_periodic";

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
        public string ColErrorCount { get; } = "error_count";
        public string ColErrorTime { get; } = "error_time";
        public string ColErrorMessage { get; } = "error_message";
        public string ColErrorStackTrace { get; } = "error_stack_trace";
        public string ColCronExpression { get; } = "cron_expression";
        public string ColUpdatedAt { get; } = "updated_at";
        public string ColIsActive { get; } = "is_active";

        public bool HasSchemaSupport { get; } = true;

        public string DateTimeColumnType { get; } = "timestamptz";

        public string? GetAcquireLeaseSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => null;

        public string GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
WITH cte AS (
  SELECT {ColId}, {ColQueue}, {ColScheduledAt}
  FROM {XactJobSchema}.{XactJobTable}
  WHERE {ColScheduledAt} <= current_timestamp
    AND {ColQueue} = '{queueName ?? QueueNames.Default}'
    AND ({ColLeasedUntil} IS NULL OR {ColLeasedUntil} < current_timestamp)
  ORDER BY {ColScheduledAt}
  FOR UPDATE SKIP LOCKED
  LIMIT {maxJobs}
)
UPDATE {XactJobSchema}.{XactJobTable}
SET {ColLeaser} = '{leaser}'::uuid, {ColLeasedUntil} = current_timestamp + interval '{leaseDurationInSeconds} seconds'
FROM cte
WHERE -- we need to join on all these, since these are the PK
    {XactJobSchema}.{XactJobTable}.{ColId} = cte.{ColId}
    AND {XactJobSchema}.{XactJobTable}.{ColQueue} = cte.{ColQueue}
    AND {XactJobSchema}.{XactJobTable}.{ColScheduledAt} = cte.{ColScheduledAt}
RETURNING {XactJobSchema}.{XactJobTable}.*
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE {XactJobSchema}.{XactJobTable}
SET {ColLeasedUntil} = current_timestamp + interval '{leaseDurationInSeconds} seconds'
WHERE {ColLeaser} = '{leaser}'::uuid
";

        public string GetClearLeaseSql(Guid leaser) => $@"
UPDATE {XactJobSchema}.{XactJobTable}
SET {ColLeaser} = NULL, {ColLeasedUntil} = NULL
WHERE {ColLeaser} = '{leaser}'::uuid
";

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
