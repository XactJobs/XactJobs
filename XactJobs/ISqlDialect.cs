namespace XactJobs
{
    public interface ISqlDialect
    {
        string GetFetchJobsSql(int maxJobs);
    }

    public class PostgresDialect : ISqlDialect
    {
        public string GetFetchJobsSql(int maxJobs) => $@"
SELECT * FROM ""{Names.TableXactJob}""
WHERE ""{Names.ColStatus}"" IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
    AND ""{Names.ColScheduledAt}"" <= current_timestamp
ORDER BY ""{Names.ColScheduledAt}""
FOR UPDATE SKIP LOCKED
LIMIT {maxJobs}
";
    }

    public class MsSqlDialect : ISqlDialect
    {
        public string GetFetchJobsSql(int maxJobs) => $@"
SELECT TOP ({maxJobs}) *
FROM [{Names.TableXactJob}] WITH (ROWLOCK, READPAST, UPDLOCK)
WHERE [{Names.ColStatus}] IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
    AND [{Names.ColScheduledAt}] <= getutcdate()
ORDER BY [{Names.ColScheduledAt}]
";
    }

    public class MySqlDialect : ISqlDialect
    {
        public string GetFetchJobsSql(int maxJobs) => $@"
SELECT * FROM `{Names.TableXactJob}`
WHERE `{Names.ColStatus}` IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
    AND `{Names.ColScheduledAt}` <= utc_timestamp()
ORDER BY `{Names.ColScheduledAt}`
LIMIT {maxJobs} FOR UPDATE SKIP LOCKED
";
    }

    public class OracleDialect : ISqlDialect
    {
        public string GetFetchJobsSql(int maxJobs) => $@"
SELECT * FROM {Names.TableXactJob}
WHERE {Names.ColStatus} IN ({(int)XactJobStatus.Queued}, {(int)XactJobStatus.Failed})
    AND {Names.ColScheduledAt} <= sys_extract_utc(current_timestamp)
ORDER BY {Names.ColScheduledAt}
FOR UPDATE SKIP LOCKED
FETCH FIRST {maxJobs} ROWS ONLY";
    }
}
