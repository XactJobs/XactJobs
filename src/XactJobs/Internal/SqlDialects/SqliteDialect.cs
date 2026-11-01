using Microsoft.EntityFrameworkCore;

namespace XactJobs.Internal.SqlDialects
{
    public class SqliteDialect : ISqlDialect
    {
        public string XactJobSchema { get; } = "xact_jobs";
        public string XactJobTable { get; } = "job";
        public string XactJobHistoryTable { get; } = "job_history";
        public string XactJobPeriodicTable { get; } = "job_periodic";

        public string PrimaryKeyPrefix { get; } = "pk";
        public string IndexPrefix { get; } = "ix";

        public string ColId { get; } = "id";
        public string ColCreatedAt { get; } = "created_at";
        public string ColScheduledAt { get; } = "scheduled_at";
        public string ColProcessedAt { get; } = "processed_at";
        public string ColLeasedUntil { get; } = "leased_until";
        public string ColLeaser { get; } = "leaser";
        public string ColTypeName { get; } = "type_name";
        public string ColMethodName { get; } = "method_name";
        public string ColMethodArgs { get; } = "method_args";
        public string ColStatus { get; } = "status";
        public string ColQueue { get; } = "queue";
        public string ColPeriodicJobId { get; } = "periodic_job_id";
        public string ColPeriodicJobVersion { get; } = "periodic_job_version";
        public string ColErrorCount { get; } = "error_count";
        public string ColErrorTime { get; } = "error_time";
        public string ColErrorMessage { get; } = "error_message";
        public string ColErrorStackTrace { get; } = "error_stack_trace";
        public string ColCronExpression { get; } = "cron_expression";
        public string ColUpdatedAt { get; } = "updated_at";
        public string ColIsActive { get; } = "is_active";
        public string ColVersion { get; } = "version";

        public bool HasSchemaSupport { get; } = false;

        public string DateTimeColumnType { get; } = "DATETIME"; // SQLite stores datetimes as ISO8601 text

        public Task<List<XactJob>> ClaimJobsAsync(DbContext db, string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds, CancellationToken cancellationToken)
        {
            return SqlDialectExtensions.ClaimJobsInOneStatementAsync(db, GetFetchJobsSql(queueName, maxJobs, leaser, leaseDurationInSeconds), cancellationToken);
        }

        // The leaser is passed as a string, because the provider would bind a Guid in uppercase.
        // All the statements must write and compare it in the same case.
        public (string Sql, object[] Parameters) GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => ($@"
WITH cte AS (
  SELECT {ColId}, {ColQueue}, {ColScheduledAt}
  FROM {XactJobSchema}_{XactJobTable}
  WHERE {ColScheduledAt} <= datetime('subsec')
    AND {ColQueue} = {{0}}
    AND ({ColLeasedUntil} IS NULL OR {ColLeasedUntil} < datetime('subsec'))
  ORDER BY {ColScheduledAt}
  LIMIT {{1}}
)
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeaser} = {{2}},
    {ColLeasedUntil} = datetime('subsec', '+' || {{3}} || ' seconds')
FROM cte
WHERE
    {XactJobSchema}_{XactJobTable}.{ColId} = cte.{ColId}
    AND {XactJobSchema}_{XactJobTable}.{ColQueue} = cte.{ColQueue}
    AND {XactJobSchema}_{XactJobTable}.{ColScheduledAt} = cte.{ColScheduledAt}
RETURNING *
", [queueName ?? QueueNames.Default, maxJobs, leaser.ToString(), leaseDurationInSeconds]);

        // The primary key is just the id on Sqlite (see XactJobEntityConfiguration).
        public (string Sql, object[] Parameters) GetExtendLeaseSql(Guid leaser, IReadOnlyCollection<XactJob> jobs, int leaseDurationInSeconds)
        {
            List<object> parameters = [leaseDurationInSeconds, leaser.ToString()];

            var jobIds = BuildJobIdList(jobs, parameters);

            return ($@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeasedUntil} = datetime('subsec', '+' || {{0}} || ' seconds')
WHERE {ColLeaser} = {{1}}
  AND {ColId} IN ({jobIds})
", parameters.ToArray());
        }

        public (string Sql, object[] Parameters) GetClearLeaseSql(Guid leaser, IReadOnlyCollection<XactJob> jobs)
        {
            List<object> parameters = [leaser.ToString()];

            var jobIds = BuildJobIdList(jobs, parameters);

            return ($@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeaser} = NULL, {ColLeasedUntil} = NULL
WHERE {ColLeaser} = {{0}}
  AND {ColId} IN ({jobIds})
", parameters.ToArray());
        }

        private static string BuildJobIdList(IReadOnlyCollection<XactJob> jobs, List<object> parameters)
        {
            var placeholders = new List<string>(jobs.Count);

            foreach (var job in jobs)
            {
                placeholders.Add($"{{{parameters.Count}}}");
                parameters.Add(job.Id);
            }

            return string.Join(", ", placeholders);
        }

        public Task AcquireTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ReleaseTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}

