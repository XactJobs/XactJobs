using UUIDNext;

namespace XactJobs.SqlDialects
{
    public class PostgreSqlDialect : ISqlDialect
    {
        public string DateTimeColumnType { get; } = "timestamptz";

        public Guid NewJobId() => Uuid.NewDatabaseFriendly(Database.PostgreSql);

        public string? GetAcquireLeaseSql(int maxJobs, Guid leaser, int leaseDurationInSeconds) => null;

        public string GetFetchJobsSql(int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
WITH cte AS (
  SELECT ""{Names.ColId}""
  FROM ""{Names.XactJobSchema}"".""{Names.XactJobTable}""
  WHERE ""{Names.ColStatus}"" IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
    AND (""{Names.ColLeasedUntil}"" IS NULL OR ""{Names.ColLeasedUntil}"" < current_timestamp)
  ORDER BY ""{Names.ColId}""
  FOR UPDATE SKIP LOCKED
  LIMIT {maxJobs}
)
UPDATE ""{Names.XactJobSchema}"".""{Names.XactJobTable}""
SET ""{Names.ColLeaser}"" = '{leaser}'::uuid, ""{Names.ColLeasedUntil}"" = current_timestamp + interval '{leaseDurationInSeconds} seconds'
FROM cte
WHERE ""{Names.XactJobSchema}"".""{Names.XactJobTable}"".""{Names.ColId}"" = cte.""{Names.ColId}"";
RETURNING ""{Names.XactJobSchema}"".""{Names.XactJobTable}"".*
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE ""{Names.XactJobSchema}"".""{Names.XactJobTable}""
SET ""{Names.ColLeasedUntil}"" = current_timestamp + interval '{leaseDurationInSeconds} seconds'
WHERE ""{Names.ColLeaser}"" = '{leaser}'::uuid
  AND ""{Names.ColStatus}"" IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed});
";
    }
}
