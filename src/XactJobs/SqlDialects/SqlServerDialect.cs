using Microsoft.EntityFrameworkCore;
using UUIDNext;

namespace XactJobs.SqlDialects
{
    public class SqlServerDialect : ISqlDialect
    {
        public bool HasSchemaSupport { get; } = true;

        public string XactJobSchema { get; } = "XactJobs";
        public string XactJobTable { get; } = "Job";
        public string XactJobHistoryTable { get; } = "JobHistory";
        public string XactJobPeriodicTable { get; } = "JobPeriodic";

        public string ColId { get; } = "Id";
        public string ColCreatedAt { get; } = "CreatedAt";
        public string ColScheduledAt { get; } = "ScheduledAt";
        public string ColCompletedAt { get; } = "CompletedAt";
        public string ColLeasedUntil { get; } = "LeasedUntil";
        public string ColLeaser { get; } = "Leaser";
        public string ColTypeName { get; } = "TypeName";
        public string ColMethodName { get; } = "MethodName";
        public string ColMethodArgs { get; } = "MethodArgs";
        public string ColStatus { get; } = "Status";
        public string ColQueue { get; } = "Queue";
        public string ColPeriodicJobId { get; } = "PeriodicJobId";
        public string ColPeriodicJobName { get; } = "PeriodicJobName";
        public string ColErrorCount { get; } = "ErrorCount";
        public string ColErrorTime { get; } = "ErrorTime";
        public string ColErrorMessage { get; } = "ErrorMessage";
        public string ColErrorStackTrace { get; } = "ErrorStackTrace";
        public string ColCronExpression { get; } = "CronExpression";
        public string ColName { get; } = "Name";
        public string ColUpdatedAt { get; } = "UpdatedAt";
        public string ColIsActive { get; } = "IsActive";

        public string DateTimeColumnType { get; } = "datetime2";

        public Guid NewJobId() => Uuid.NewDatabaseFriendly(Database.SqlServer);

        public string? GetAcquireLeaseSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => null;

        public string GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
WITH cte AS (
    SELECT TOP ({maxJobs}) [{ColId}]
    FROM [{XactJobSchema}].[{XactJobTable}] WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE [{ColStatus}] IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
      AND [{ColScheduledAt}] <= SYSUTCDATETIME()
      AND [{ColQueue}] = '{queueName ?? QueueNames.Default}'
      AND ([{ColLeasedUntil}] IS NULL OR [{ColLeasedUntil}] < SYSUTCDATETIME())
    ORDER BY [{ColScheduledAt}]
)
UPDATE target
SET [{ColLeaser}] = CAST('{leaser}' AS UNIQUEIDENTIFIER),
    [{ColLeasedUntil}] = DATEADD(SECOND, {leaseDurationInSeconds}, SYSUTCDATETIME())
OUTPUT inserted.*
FROM [{XactJobSchema}].[{XactJobTable}] AS target
INNER JOIN cte ON target.[{ColId}] = cte.[{ColId}]
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE [{XactJobSchema}].[{XactJobTable}]
SET [{ColLeasedUntil}] = DATEADD(SECOND, {leaseDurationInSeconds}, SYSUTCDATETIME())
WHERE [{ColLeaser}] = CAST('{leaser}' AS UNIQUEIDENTIFIER)
  AND [{ColStatus}] IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";

        public string GetClearLeaseSql(Guid leaser) => $@"
UPDATE [{XactJobSchema}].[{XactJobTable}]
SET [{ColLeaser}] = NULL, [{ColLeasedUntil}] = NULL
WHERE [{ColLeaser}] = CAST('{leaser}' AS UNIQUEIDENTIFIER)
  AND [{ColStatus}] IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";

        public async Task AcquireTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            var tableLockName = $"[{XactJobSchema}].[{XactJobPeriodicTable}]";

            var result = await db.Database.ExecuteOutputIntAsync(@$"
EXEC @result = sp_getapplock @Resource = '{tableLockName}', @LockMode = 'Exclusive', @LockTimeout = 30000
", cancellationToken)
                .ConfigureAwait(false);

            if (result < 0) throw new Exception($"Failed to acquire lock (LockName='{tableLockName}', Result={result})");
        }

        public Task ReleaseTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
