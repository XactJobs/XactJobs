using Microsoft.EntityFrameworkCore;
using UUIDNext;

namespace XactJobs.SqlDialects
{
    public class OracleDialect : ISqlDialect
    {
        public bool HasSchemaSupport { get; } = true;
        public string SchemaName { get; } = Names.XactJobSchema.ToUpperInvariant();

        public string DateTimeColumnType { get; } = "TIMESTAMP";

        public Guid NewJobId() => Uuid.NewDatabaseFriendly(Database.Other);

        public string? GetAcquireLeaseSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE ""{SchemaName}"".""{Names.XactJobTable}"" tgt
SET ""{Names.ColLeaser}"" = HEXTORAW('{leaser:N}'),
    ""{Names.ColLeasedUntil}"" = SYS_EXTRACT_UTC(systimestamp) + INTERVAL '{leaseDurationInSeconds}' SECOND
WHERE tgt.""{Names.ColId}"" IN (
    SELECT ""{Names.ColId}""
    FROM ""{SchemaName}"".""{Names.XactJobTable}""
    WHERE ""{Names.ColStatus}"" IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
      AND ""{Names.ColScheduledAt}"" <= SYS_EXTRACT_UTC(systimestamp)
      AND ""{Names.ColQueue}"" = '{queueName ?? Names.QueueDefault}'
      AND (""{Names.ColLeasedUntil}"" IS NULL OR ""{Names.ColLeasedUntil}"" < SYS_EXTRACT_UTC(systimestamp))
      AND ROWNUM <= {maxJobs}
)
";

        public string GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
SELECT *
FROM ""{SchemaName}"".""{Names.XactJobTable}""
WHERE ""{Names.ColLeaser}"" = HEXTORAW('{leaser:N}')
  AND ""{Names.ColLeasedUntil}"" >= SYS_EXTRACT_UTC(systimestamp)
  AND ""{Names.ColQueue}"" = '{queueName ?? Names.QueueDefault}'
  AND ROWNUM <= {maxJobs}
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE ""{SchemaName}"".""{Names.XactJobTable}""
SET ""{Names.ColLeasedUntil}"" = SYS_EXTRACT_UTC(systimestamp) + INTERVAL '{leaseDurationInSeconds}' SECOND
WHERE ""{Names.ColLeaser}"" = HEXTORAW('{leaser:N}')
  AND ""{Names.ColStatus}"" IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";

        public string GetClearLeaseSql(Guid leaser) => $@"
UPDATE ""{SchemaName}"".""{Names.XactJobTable}""
SET ""{Names.ColLeaser}"" = NULL, ""{Names.ColLeasedUntil}"" = NULL
WHERE ""{Names.ColLeaser}"" = HEXTORAW('{leaser:N}')
  AND ""{Names.ColStatus}"" IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";

        public async Task AcquireTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            var sql = $@"LOCK TABLE ""{SchemaName}"".""{Names.XactJobPeriodicTable}"" IN EXCLUSIVE MODE";

            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task ReleaseTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
