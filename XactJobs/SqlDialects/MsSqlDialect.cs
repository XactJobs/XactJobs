namespace XactJobs.SqlDialects
{
    public class MsSqlDialect : ISqlDialect
    {
        public string DateTimeColumnType { get; } = "datetime2";

        public string? GetAcquireLeaseSql(int maxJobs, Guid leaser, int leaseDurationInSeconds) => null;

        public string GetFetchJobsSql(int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
WITH cte AS (
    SELECT TOP ({maxJobs}) [{Names.ColId}]
    FROM [{Names.XactJobSchema}].[{Names.XactJobTable}] WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE [{Names.ColStatus}] IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
      AND ([{Names.ColLeasedUntil}] IS NULL OR [{Names.ColLeasedUntil}] < SYSUTCDATETIME())
    ORDER BY [{Names.ColId}]
)
UPDATE target
SET [{Names.ColLeaser}] = CAST('{leaser}' AS UNIQUEIDENTIFIER),
    [{Names.ColLeasedUntil}] = DATEADD(SECOND, {leaseDurationInSeconds}, SYSUTCDATETIME())
OUTPUT inserted.*
FROM [{Names.XactJobSchema}].[{Names.XactJobTable}] AS target
INNER JOIN cte ON target.[{Names.ColId}] = cte.[{Names.ColId}];
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE [{Names.XactJobSchema}].[{Names.XactJobTable}]
SET leased_until = DATEADD(SECOND, {leaseDurationInSeconds}, SYSUTCDATETIME())
WHERE leaser = CAST('{leaser}' AS UNIQUEIDENTIFIER)
  AND status IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed});
";
    }
}
