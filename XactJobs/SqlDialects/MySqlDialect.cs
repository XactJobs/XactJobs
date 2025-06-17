using UUIDNext;

namespace XactJobs.SqlDialects
{
    public class MySqlDialect : ISqlDialect
    {
        public string DateTimeColumnType { get; } = "datetime(6)";

        public Guid NewJobId() => Uuid.NewDatabaseFriendly(Database.Other);

        public string? GetAcquireLeaseSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE `{Names.XactJobSchema}`.`{Names.XactJobTable}`
SET `{Names.ColLeaser}` = '{leaser}',
    `{Names.ColLeasedUntil}` = UTC_TIMESTAMP + INTERVAL {leaseDurationInSeconds} SECOND
WHERE `{Names.ColId}` IN (
    SELECT `{Names.ColId}`
    FROM `{Names.XactJobSchema}`.`{Names.XactJobTable}`
    WHERE `{Names.ColStatus}` IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
      AND `{Names.ColScheduledAt}` <= UTC_TIMESTAMP 
      AND `{Names.ColQueue}` = '{queueName ?? Names.QueueDefault}'
      AND (`{Names.ColLeasedUntil}` IS NULL OR `{Names.ColLeasedUntil}` < UTC_TIMESTAMP)
    ORDER BY `{Names.ColScheduledAt}`
    LIMIT {maxJobs}
    FOR UPDATE SKIP LOCKED
)
";

        public string GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
SELECT *
FROM `{Names.XactJobSchema}`.`{Names.XactJobTable}`
WHERE `{Names.ColLeaser}` = '{leaser}'
  AND `{Names.ColLeasedUntil}` > UTC_TIMESTAMP
  AND `{Names.ColQueue}` = '{queueName ?? Names.QueueDefault}'
LIMIT {maxJobs}
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE `{Names.XactJobSchema}`.`{Names.XactJobTable}`
SET `{Names.ColLeasedUntil}` = UTC_TIMESTAMP + INTERVAL {leaseDurationInSeconds} SECOND
WHERE `{Names.ColLeaser}` = '{leaser}'
  AND `{Names.ColStatus}` IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";

        public string GetClearLeaseSql(Guid leaser) => $@"
UPDATE `{Names.XactJobSchema}`.`{Names.XactJobTable}`
SET `{Names.ColLeaser}` = NULL, `{Names.ColLeasedUntil}` = NULL
WHERE `{Names.ColLeaser}` = '{leaser}'
  AND `{Names.ColStatus}` IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
";

        public string GetLockJobPeriodicSql() => $@"
SELECT GET_LOCK('{Names.XactJobSchema}.{Names.XactJobPeriodicTable}', 30);
";

    }
}
