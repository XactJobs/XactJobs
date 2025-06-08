using UUIDNext;

namespace XactJobs.SqlDialects
{
    public class SqlServerDialect : ISqlDialect
    {
        public string DateTimeColumnType { get; } = "datetime2";

        public Guid NewJobId() => Uuid.NewDatabaseFriendly(Database.SqlServer);

        public string? GetAcquireLeaseSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => null;

        public string GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
WITH cte AS (
    SELECT TOP ({maxJobs}) [{Names.ColId}]
    FROM [{Names.XactJobSchema}].[{Names.XactJobTable}] WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE [{Names.ColStatus}] IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
      AND [{Names.ColScheduledAt}] <= SYSUTCDATETIME()
      AND [{Names.ColQueue}] = '{queueName ?? Names.DefaultQueue}'
      AND ([{Names.ColLeasedUntil}] IS NULL OR [{Names.ColLeasedUntil}] < SYSUTCDATETIME())
    ORDER BY [{Names.ColScheduledAt}]
)
UPDATE target
SET [{Names.ColLeaser}] = CAST('{leaser}' AS UNIQUEIDENTIFIER),
    [{Names.ColLeasedUntil}] = DATEADD(SECOND, {leaseDurationInSeconds}, SYSUTCDATETIME())
OUTPUT inserted.*
FROM [{Names.XactJobSchema}].[{Names.XactJobTable}] AS target
INNER JOIN cte ON target.[{Names.ColId}] = cte.[{Names.ColId}]
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE [{Names.XactJobSchema}].[{Names.XactJobTable}]
SET [{Names.ColLeasedUntil}] = DATEADD(SECOND, {leaseDurationInSeconds}, SYSUTCDATETIME())
WHERE [{Names.ColLeaser}] = CAST('{leaser}' AS UNIQUEIDENTIFIER)
  AND [{Names.ColStatus}] IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";

        public string GetClearLeaseSql(Guid leaser) => $@"
UPDATE [{Names.XactJobSchema}].[{Names.XactJobTable}]
SET [{Names.ColLeaser}] = NULL, [{Names.ColLeasedUntil}] = NULL
WHERE [{Names.ColLeaser}] = CAST('{leaser}' AS UNIQUEIDENTIFIER)
  AND [{Names.ColStatus}] IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";
    }
}
