namespace XactJobs.SqlDialects
{
    public class MySqlDialect : ISqlDialect
    {
        public string DateTimeColumnType { get; } = "datetime(6)";

        public string? GetAcquireLeaseSql(int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE `{Names.XactJobSchema}`.`{Names.XactJobTable}`
SET `{Names.ColLeaser}` = '{leaser}',
    `{Names.ColLeasedUntil}` = UTC_TIMESTAMP + INTERVAL {leaseDurationInSeconds} SECOND
WHERE `{Names.ColId}` IN (
    SELECT `{Names.ColId}`
    FROM `{Names.XactJobSchema}`.`{Names.XactJobTable}`
    WHERE `{Names.ColStatus}` IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
      AND (`{Names.ColLeasedUntil}` IS NULL OR `{Names.ColLeasedUntil}` < UTC_TIMESTAMP)
    ORDER BY `{Names.ColId}`
    LIMIT {maxJobs}
    FOR UPDATE SKIP LOCKED
);
";

        public string GetFetchJobsSql(int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
SELECT *
FROM `{Names.XactJobSchema}`.`{Names.XactJobTable}`
WHERE `{Names.ColLeaser}` = '{leaser}'
  AND `{Names.ColLeasedUntil}` > UTC_TIMESTAMP
LIMIT {maxJobs};
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE `{Names.XactJobSchema}`.`{Names.XactJobTable}`
SET leased_until = UTC_TIMESTAMP + INTERVAL {leaseDurationInSeconds} SECOND
WHERE leaser = '{leaser}'
  AND status IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed});
";
    }
}
