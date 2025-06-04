using UUIDNext;

namespace XactJobs.SqlDialects
{
    public class OracleDialect : ISqlDialect
    {
        public string DateTimeColumnType { get; } = "timestamp with time zone";

        public Guid NewJobId() => Uuid.NewDatabaseFriendly(Database.Other);

        public string? GetAcquireLeaseSql(int maxJobs, Guid leaser, int leaseDurationInSeconds) => null;

        public string GetFetchJobsSql(int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
WITH cte AS (
    SELECT {Names.ColId}
    FROM {Names.XactJobSchema}.{Names.XactJobTable}
    WHERE {Names.ColStatus} IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
      AND ({Names.ColLeasedUntil} IS NULL OR {Names.ColLeasedUntil} < SYSTIMESTAMP AT TIME ZONE 'UTC')
    ORDER BY {Names.ColId}
    FOR UPDATE SKIP LOCKED
    FETCH FIRST {maxJobs} ROWS ONLY
)
UPDATE {Names.XactJobSchema}.{Names.XactJobTable} tgt
SET {Names.ColLeaser} = HEXTORAW('{leaser:N}'),
    {Names.ColLeasedUntil} = SYSTIMESTAMP AT TIME ZONE 'UTC' + INTERVAL '{leaseDurationInSeconds}' SECOND
WHERE tgt.{Names.ColId} IN (SELECT {Names.ColId} FROM cte)
RETURNING tgt.*;
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE {Names.XactJobSchema}.{Names.XactJobTable}
SET leased_until = SYSTIMESTAMP + NUMTODSINTERVAL({leaseDurationInSeconds}, 'SECOND')
WHERE leaser = HEXTORAW('{leaser:N}')
  AND status IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed});
";
    }
}
