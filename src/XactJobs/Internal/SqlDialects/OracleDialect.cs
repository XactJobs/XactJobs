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
using System.Data;

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
        public string ColBatchId { get; } = "BATCH_ID";
        public string ColAwaitsBatchId { get; } = "AWAITS_BATCH_ID";
        public string ColContinuationCondition { get; } = "CONTINUATION_CONDITION";

        public string DateTimeColumnType { get; } = "TIMESTAMP";

        public IsolationLevel? ClaimIsolationLevel { get; } = null;

        public Task<List<XactJob>> ClaimJobsAsync(DbContext db, string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds, CancellationToken cancellationToken)
        {
            // BULK COLLECT needs a positive LIMIT
            if (maxJobs <= 0) return Task.FromResult(new List<XactJob>());

            return SqlDialectExtensions.ClaimJobsInOneStatementAsync(db, GetFetchJobsSql(queueName, maxJobs, leaser, leaseDurationInSeconds), cancellationToken);
        }

        // the due jobs, oldest first (see GetFetchSql)
        public (string Sql, object[] Parameters) GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => GetFetchSql($@"
    SELECT /*+ INDEX_ASC({Table} ({ColQueue}, {ColScheduledAt}, {ColId})) */ ROWIDTOCHAR(ROWID)
    FROM {Table}
    WHERE {ColScheduledAt} <= SYS_EXTRACT_UTC(systimestamp)
      AND {ColQueue} = {{0}}
      AND ({ColLeasedUntil} IS NULL OR {ColLeasedUntil} < SYS_EXTRACT_UTC(systimestamp))
    ORDER BY {ColScheduledAt}
    FOR UPDATE SKIP LOCKED", [queueName ?? QueueNames.Default], ColScheduledAt, maxJobs, leaser, leaseDurationInSeconds);

        public Task<List<XactJob>> ClaimContinuationsAsync(DbContext db, string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds, CancellationToken cancellationToken)
        {
            // BULK COLLECT needs a positive LIMIT
            if (maxJobs <= 0) return Task.FromResult(new List<XactJob>());

            return SqlDialectExtensions.ClaimJobsInOneStatementAsync(db, GetFetchContinuationsSql(queueName, maxJobs, leaser, leaseDurationInSeconds), cancellationToken);
        }

        // the continuations whose batch has no jobs left, in id order (see GetFetchSql). Only the rows of the outer query are locked:
        // the batch's jobs are a consistent read, which doesn't skip rows.
        public (string Sql, object[] Parameters) GetFetchContinuationsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => GetFetchSql($@"
    SELECT /*+ INDEX_ASC(c ({ColQueue}, {ColScheduledAt}, {ColId})) */ ROWIDTOCHAR(c.ROWID)
    FROM {Table} c
    WHERE c.{ColQueue} = {{0}}
      AND c.{ColScheduledAt} = {{1}}
      AND c.{ColAwaitsBatchId} IS NOT NULL
      AND (c.{ColLeasedUntil} IS NULL OR c.{ColLeasedUntil} < SYS_EXTRACT_UTC(systimestamp))
      AND NOT EXISTS (SELECT 1 FROM {Table} b WHERE b.{ColBatchId} = c.{ColAwaitsBatchId})
    ORDER BY c.{ColId}
    FOR UPDATE SKIP LOCKED", [queueName ?? QueueNames.Default, XactJobBase.AwaitingBatchScheduledAt], ColId, maxJobs, leaser, leaseDurationInSeconds);

        // SYS.ODCIVARCHAR2LIST, which holds the claimed rows, is a VARRAY(32767)
        private const int MaxJobsPerClaim = 32767;

        /// <summary>
        /// Claims up to <paramref name="maxJobs"/> rows of <paramref name="lockRows"/> (a query of ROWIDs, FOR UPDATE SKIP LOCKED), in one
        /// PL/SQL block, and returns them as they were before the lease, in <paramref name="orderBy"/> order.
        /// <para>
        /// ROWNUM and FETCH FIRST can't limit the rows: ROWNUM is applied before locked rows are skipped, so workers polling at once
        /// would all go for the same first rows, and all but one would get none. Oracle doesn't allow FETCH FIRST with FOR UPDATE.
        /// With SKIP LOCKED, Oracle locks a row when it's fetched, so the block fetches maxJobs rows (BULK COLLECT LIMIT) and closes
        /// the cursor: the rows after them aren't locked. The fetch is on the server: ODP.NET's prefetch, which on the first execution
        /// of a statement on a connection ignores FetchSize, would lock far more rows, until the claim commits.
        /// </para>
        /// <para>
        /// The hint reads the primary key in order, instead of sorting all the due rows of the queue. The result cursor is opened
        /// before the lease, so it reads the rows as they were then (like the other dialects that lock, then lease).
        /// DBMS_SQL.RETURN_RESULT needs Oracle 12c or later.
        /// </para>
        /// </summary>
        private (string Sql, object[] Parameters) GetFetchSql(string lockRows, object[] lockParameters, string orderBy, int maxJobs, Guid leaser, int leaseDurationInSeconds)
        {
            var limit = lockParameters.Length;
            var leaserParameter = limit + 1;
            var durationParameter = limit + 2;

            return ($@"
DECLARE
  CURSOR claimable IS{lockRows};
  claimed SYS.ODCIVARCHAR2LIST;
  result SYS_REFCURSOR;
BEGIN
  OPEN claimable;
  FETCH claimable BULK COLLECT INTO claimed LIMIT {{{limit}}};
  CLOSE claimable;

  OPEN result FOR
    SELECT *
    FROM {Table}
    WHERE ROWID IN (SELECT CHARTOROWID(COLUMN_VALUE) FROM TABLE(claimed))
    ORDER BY {orderBy};

  FORALL i IN 1 .. claimed.COUNT
    UPDATE {Table}
    SET {ColLeaser} = HEXTORAW({{{leaserParameter}}}),
        {ColLeasedUntil} = SYS_EXTRACT_UTC(systimestamp) + NUMTODSINTERVAL({{{durationParameter}}}, 'SECOND')
    WHERE ROWID = CHARTOROWID(claimed(i));

  DBMS_SQL.RETURN_RESULT(result);
END;", [.. lockParameters, Math.Min(maxJobs, MaxJobsPerClaim), leaser.ToString("N"), leaseDurationInSeconds]);
        }

        private string Table => $"{XactJobSchema}_{XactJobTable}";

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
