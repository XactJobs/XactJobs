using Microsoft.EntityFrameworkCore;
using UUIDNext;

namespace XactJobs.SqlDialects
{
    public class MySqlDialect : ISqlDialect
    {
        public bool HasSchemaSupport { get; } = false;
        public string DateTimeColumnType { get; } = "datetime(6)";

        public Guid NewJobId() => Uuid.NewDatabaseFriendly(Database.Other);

        public string? GetAcquireLeaseSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE `{Names.XactJobSchema}__{Names.XactJobTable}` AS t
JOIN (
    SELECT `{Names.ColId}`
    FROM `{Names.XactJobSchema}__{Names.XactJobTable}`
    WHERE `{Names.ColStatus}` IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
      AND `{Names.ColScheduledAt}` <= UTC_TIMESTAMP 
      AND `{Names.ColQueue}` = '{queueName ?? Names.QueueDefault}'
      AND (`{Names.ColLeasedUntil}` IS NULL OR `{Names.ColLeasedUntil}` < UTC_TIMESTAMP)
    ORDER BY `{Names.ColScheduledAt}`
    LIMIT {maxJobs}
    FOR UPDATE SKIP LOCKED
) AS sub ON t.`{Names.ColId}` = sub.`{Names.ColId}`
SET t.`{Names.ColLeaser}` = '{leaser}',
    t.`{Names.ColLeasedUntil}` = UTC_TIMESTAMP + INTERVAL {leaseDurationInSeconds} SECOND
";

        public string GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
SELECT *
FROM `{Names.XactJobSchema}__{Names.XactJobTable}`
WHERE `{Names.ColLeaser}` = '{leaser}'
  AND `{Names.ColLeasedUntil}` > UTC_TIMESTAMP
  AND `{Names.ColQueue}` = '{queueName ?? Names.QueueDefault}'
LIMIT {maxJobs}
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE `{Names.XactJobSchema}__{Names.XactJobTable}`
SET `{Names.ColLeasedUntil}` = UTC_TIMESTAMP + INTERVAL {leaseDurationInSeconds} SECOND
WHERE `{Names.ColLeaser}` = '{leaser}'
  AND `{Names.ColStatus}` IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";

        public string GetClearLeaseSql(Guid leaser) => $@"
UPDATE `{Names.XactJobSchema}__{Names.XactJobTable}`
SET `{Names.ColLeaser}` = NULL, `{Names.ColLeasedUntil}` = NULL
WHERE `{Names.ColLeaser}` = '{leaser}'
  AND `{Names.ColStatus}` IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";

        public async Task AcquireTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            var tableLockName = $"{tableSchema}__{tableName}";

            var result = await db.Database.ExecuteScalarIntAsync($"SELECT GET_LOCK('{tableLockName}', 30)", cancellationToken)
                .ConfigureAwait(false);

            if (result != 1) throw new Exception($"Failed to ackquire lock (LockName='{tableLockName}', Result={result})");
        }

        public async Task ReleaseTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            var tableLockName = $"{tableSchema}__{tableName}";

            var result = await db.Database.ExecuteScalarIntAsync($"SELECT RELEASE_LOCK('{tableLockName}')", cancellationToken)
                .ConfigureAwait(false);

            if (result != 1) throw new Exception($"Failed to release lock (LockName='{tableLockName}', Result={result})");
        }
    }
}
