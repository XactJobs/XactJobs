using Microsoft.EntityFrameworkCore;
using UUIDNext;

namespace XactJobs.SqlDialects
{
    public class OracleDialect : ISqlDialect
    {
        public bool HasSchemaSupport { get; } = false;

        public string XactJobSchema { get; } = "XACT_JOBS";
        public string XactJobTable { get; } = "JOB";
        public string XactJobHistoryTable { get; } = "JOB_HISTORY";
        public string XactJobPeriodicTable { get; } = "JOB_PERIODIC";

        public string ColId { get; } = "ID";
        public string ColCreatedAt { get; } = "CREATED_AT";
        public string ColScheduledAt { get; } = "SCHEDULED_AT";
        public string ColCompletedAt { get; } = "COMPLETED_AT";
        public string ColLeasedUntil { get; } = "LEASED_UNTIL";
        public string ColLeaser { get; } = "LEASER";
        public string ColTypeName { get; } = "TYPE_NAME";
        public string ColMethodName { get; } = "METHOD_NAME";
        public string ColMethodArgs { get; } = "METHOD_ARGS";
        public string ColStatus { get; } = "STATUS";
        public string ColQueue { get; } = "QUEUE";
        public string ColPeriodicJobId { get; } = "PERIODIC_JOB_ID";
        public string ColPeriodicJobName { get; } = "PERIODIC_JOB_NAME";
        public string ColErrorCount { get; } = "ERROR_COUNT";
        public string ColErrorTime { get; } = "ERROR_TIME";
        public string ColErrorMessage { get; } = "ERROR_MESSAGE";
        public string ColErrorStackTrace { get; } = "ERROR_STACK_TRACE";
        public string ColCronExpression { get; } = "CRON_EXPRESSION";
        public string ColName { get; } = "NAME";
        public string ColUpdatedAt { get; } = "UPDATED_AT";
        public string ColIsActive { get; } = "IS_ACTIVE";

        public string DateTimeColumnType { get; } = "TIMESTAMP";

        public Guid NewJobId() => Uuid.NewDatabaseFriendly(Database.Other);

        public string? GetAcquireLeaseSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE {XactJobSchema}_{XactJobTable} tgt
SET {ColLeaser} = HEXTORAW('{leaser:N}'),
    {ColLeasedUntil} = SYS_EXTRACT_UTC(systimestamp) + INTERVAL '{leaseDurationInSeconds}' SECOND
WHERE tgt.{ColId} IN (
    SELECT {ColId}
    FROM {XactJobSchema}_{XactJobTable}
    WHERE {ColStatus} IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
      AND {ColScheduledAt} <= SYS_EXTRACT_UTC(systimestamp)
      AND {ColQueue} = '{queueName ?? QueueNames.Default}'
      AND ({ColLeasedUntil} IS NULL OR {ColLeasedUntil} < SYS_EXTRACT_UTC(systimestamp))
      AND ROWNUM <= {maxJobs}
)
";

        public string GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
SELECT *
FROM {XactJobSchema}_{XactJobTable}
WHERE {ColLeaser} = HEXTORAW('{leaser:N}')
  AND {ColLeasedUntil} >= SYS_EXTRACT_UTC(systimestamp)
  AND {ColQueue} = '{queueName ?? QueueNames.Default}'
  AND ROWNUM <= {maxJobs}
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeasedUntil} = SYS_EXTRACT_UTC(systimestamp) + INTERVAL '{leaseDurationInSeconds}' SECOND
WHERE {ColLeaser} = HEXTORAW('{leaser:N}')
  AND {ColStatus} IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";

        public string GetClearLeaseSql(Guid leaser) => $@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeaser} = NULL, {ColLeasedUntil} = NULL
WHERE {ColLeaser} = HEXTORAW('{leaser:N}')
  AND {ColStatus} IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";

        public async Task AcquireTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            var sql = $@"LOCK TABLE {XactJobSchema}_{XactJobPeriodicTable} IN EXCLUSIVE MODE";

            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task ReleaseTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
