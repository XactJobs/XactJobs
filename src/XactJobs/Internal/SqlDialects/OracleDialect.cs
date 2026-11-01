// This file is part of XactJobs.
//
// XactJobs is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// XactJobs is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using Microsoft.EntityFrameworkCore;

namespace XactJobs.Internal.SqlDialects
{
    public class OracleDialect : ISqlDialect
    {
        public bool HasSchemaSupport { get; } = false;

        public string XactJobSchema { get; } = "XACT_JOBS";
        public string XactJobTable { get; } = "JOB";
        public string XactJobHistoryTable { get; } = "JOB_HISTORY";
        public string XactJobPeriodicTable { get; } = "JOB_PERIODIC";

        public string PrimaryKeyPrefix { get; } = "PK";
        public string IndexPrefix { get; } = "IX";

        public string ColId { get; } = "ID";
        public string ColCreatedAt { get; } = "CREATED_AT";
        public string ColScheduledAt { get; } = "SCHEDULED_AT";
        public string ColProcessedAt { get; } = "PROCESSED_AT";
        public string ColLeasedUntil { get; } = "LEASED_UNTIL";
        public string ColLeaser { get; } = "LEASER";
        public string ColTypeName { get; } = "TYPE_NAME";
        public string ColMethodName { get; } = "METHOD_NAME";
        public string ColMethodArgs { get; } = "METHOD_ARGS";
        public string ColStatus { get; } = "STATUS";
        public string ColQueue { get; } = "QUEUE";
        public string ColPeriodicJobId { get; } = "PERIODIC_JOB_ID";
        public string ColPeriodicJobVersion { get; } = "PERIODIC_JOB_VERSION";
        public string ColErrorCount { get; } = "ERROR_COUNT";
        public string ColErrorTime { get; } = "ERROR_TIME";
        public string ColErrorMessage { get; } = "ERROR_MESSAGE";
        public string ColErrorStackTrace { get; } = "ERROR_STACK_TRACE";
        public string ColCronExpression { get; } = "CRON_EXPRESSION";
        public string ColUpdatedAt { get; } = "UPDATED_AT";
        public string ColIsActive { get; } = "IS_ACTIVE";
        public string ColVersion { get; } = "VERSION";

        public string DateTimeColumnType { get; } = "TIMESTAMP";

        public Task<List<XactJob>> ClaimJobsAsync(DbContext db, string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds, CancellationToken cancellationToken)
        {
            return SqlDialectExtensions.ClaimJobsWithLockAsync(db,
                                                               GetLockJobsSql(queueName, maxJobs),
                                                               jobs => GetLeaseJobsSql(leaser, jobs, leaseDurationInSeconds),
                                                               cancellationToken);
        }

        // Oracle doesn't allow FOR UPDATE with an ordered subquery or FETCH FIRST, so jobs aren't claimed oldest first.
        // ROWNUM is applied before locked rows are skipped, so this can return fewer rows than maxJobs.
        public (string Sql, object[] Parameters) GetLockJobsSql(string? queueName, int maxJobs) => ($@"
SELECT *
FROM {XactJobSchema}_{XactJobTable}
WHERE {ColScheduledAt} <= SYS_EXTRACT_UTC(systimestamp)
  AND {ColQueue} = {{0}}
  AND ({ColLeasedUntil} IS NULL OR {ColLeasedUntil} < SYS_EXTRACT_UTC(systimestamp))
  AND ROWNUM <= {{1}}
FOR UPDATE SKIP LOCKED
", [queueName ?? QueueNames.Default, maxJobs]);

        // the leaser is passed as a hex string, for HEXTORAW
        public (string Sql, object[] Parameters) GetLeaseJobsSql(Guid leaser, IReadOnlyCollection<XactJob> jobs, int leaseDurationInSeconds)
        {
            List<object> parameters = [leaser.ToString("N"), leaseDurationInSeconds];

            var jobKeys = SqlDialectExtensions.BuildJobKeysPredicate(jobs, parameters, ColQueue, ColScheduledAt, ColId);

            return ($@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeaser} = HEXTORAW({{0}}),
    {ColLeasedUntil} = SYS_EXTRACT_UTC(systimestamp) + NUMTODSINTERVAL({{1}}, 'SECOND')
WHERE {jobKeys}
", parameters.ToArray());
        }

        public (string Sql, object[] Parameters) GetExtendLeaseSql(Guid leaser, IReadOnlyCollection<XactJob> jobs, int leaseDurationInSeconds)
        {
            List<object> parameters = [leaseDurationInSeconds, leaser.ToString("N")];

            var jobKeys = SqlDialectExtensions.BuildJobKeysPredicate(jobs, parameters, ColQueue, ColScheduledAt, ColId);

            return ($@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeasedUntil} = SYS_EXTRACT_UTC(systimestamp) + NUMTODSINTERVAL({{0}}, 'SECOND')
WHERE {ColLeaser} = HEXTORAW({{1}})
  AND ({jobKeys})
", parameters.ToArray());
        }

        public (string Sql, object[] Parameters) GetClearLeaseSql(Guid leaser, IReadOnlyCollection<XactJob> jobs)
        {
            List<object> parameters = [leaser.ToString("N")];

            var jobKeys = SqlDialectExtensions.BuildJobKeysPredicate(jobs, parameters, ColQueue, ColScheduledAt, ColId);

            return ($@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeaser} = NULL, {ColLeasedUntil} = NULL
WHERE {ColLeaser} = HEXTORAW({{0}})
  AND ({jobKeys})
", parameters.ToArray());
        }

        public async Task AcquireTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            var sql = $@"LOCK TABLE {XactJobSchema}_{XactJobPeriodicTable} IN EXCLUSIVE MODE";

            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken)
                .ConfigureAwait(false);
        }

        public Task ReleaseTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
