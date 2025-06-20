using UUIDNext;

namespace XactJobs.SqlDialects
{
    public class PostgreSqlDialect : ISqlDialect
    {
        public bool HasSchemaSupport { get; } = true;
        public string DateTimeColumnType { get; } = "timestamptz";

        public Guid NewJobId() => Uuid.NewDatabaseFriendly(Database.PostgreSql);

        public string? GetAcquireLeaseSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => null;

        public string GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
WITH cte AS (
  SELECT ""{Names.ColId}""
  FROM ""{Names.XactJobSchema}"".""{Names.XactJobTable}""
  WHERE ""{Names.ColStatus}"" IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
    AND ""{Names.ColScheduledAt}"" <= current_timestamp
    AND ""{Names.ColQueue}"" = '{queueName ?? Names.QueueDefault}'
    AND (""{Names.ColLeasedUntil}"" IS NULL OR ""{Names.ColLeasedUntil}"" < current_timestamp)
  ORDER BY ""{Names.ColScheduledAt}""
  FOR UPDATE SKIP LOCKED
  LIMIT {maxJobs}
)
UPDATE ""{Names.XactJobSchema}"".""{Names.XactJobTable}""
SET ""{Names.ColLeaser}"" = '{leaser}'::uuid, ""{Names.ColLeasedUntil}"" = current_timestamp + interval '{leaseDurationInSeconds} seconds'
FROM cte
WHERE ""{Names.XactJobSchema}"".""{Names.XactJobTable}"".""{Names.ColId}"" = cte.""{Names.ColId}""
RETURNING ""{Names.XactJobSchema}"".""{Names.XactJobTable}"".*
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE ""{Names.XactJobSchema}"".""{Names.XactJobTable}""
SET ""{Names.ColLeasedUntil}"" = current_timestamp + interval '{leaseDurationInSeconds} seconds'
WHERE ""{Names.ColLeaser}"" = '{leaser}'::uuid
  AND ""{Names.ColStatus}"" IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";

        public string GetClearLeaseSql(Guid leaser) => $@"
UPDATE ""{Names.XactJobSchema}"".""{Names.XactJobTable}""
SET ""{Names.ColLeaser}"" = NULL, ""{Names.ColLeasedUntil}"" = NULL
WHERE ""{Names.ColLeaser}"" = '{leaser}'::uuid
  AND ""{Names.ColStatus}"" IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";

        public string GetLockJobPeriodicSql() => $@"
LOCK TABLE ""{Names.XactJobSchema}"".""{Names.XactJobPeriodicTable}"" IN EXCLUSIVE MODE
";
    }
}
