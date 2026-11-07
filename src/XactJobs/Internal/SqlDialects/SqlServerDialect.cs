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
    public class SqlServerDialect : ISqlDialect
    {
        /// <param name="schema">The schema of XactJobs' tables, XactJobs by default.</param>
        public SqlServerDialect(string? schema = null)
        {
            XactJobSchema = SqlDialectExtensions.CheckSchemaName(schema, "XactJobs", @"^[A-Za-z_][A-Za-z0-9_]{0,127}$");
        }

        public bool HasSchemaSupport { get; } = true;

        public string XactJobSchema { get; }
        public string XactJobTable { get; } = "Job";
        public string XactJobHistoryTable { get; } = "JobHistory";
        public string XactJobPeriodicTable { get; } = "JobPeriodic";

        public string PrimaryKeyPrefix { get; } = "PK";
        public string IndexPrefix { get; } = "IX";

        public string ColId { get; } = "Id";
        public string ColCreatedAt { get; } = "CreatedAt";
        public string ColScheduledAt { get; } = "ScheduledAt";
        public string ColProcessedAt { get; } = "ProcessedAt";
        public string ColLeasedUntil { get; } = "LeasedUntil";
        public string ColLeaser { get; } = "Leaser";
        public string ColTypeName { get; } = "TypeName";
        public string ColMethodName { get; } = "MethodName";
        public string ColMethodArgs { get; } = "MethodArgs";
        public string ColStatus { get; } = "Status";
        public string ColQueue { get; } = "Queue";
        public string ColPeriodicJobId { get; } = "PeriodicJobId";
        public string ColPeriodicJobVersion { get; } = "PeriodicJobVersion";
        public string ColErrorCount { get; } = "ErrorCount";
        public string ColErrorTime { get; } = "ErrorTime";
        public string ColErrorMessage { get; } = "ErrorMessage";
        public string ColErrorStackTrace { get; } = "ErrorStackTrace";
        public string ColCronExpression { get; } = "CronExpression";
        public string ColUpdatedAt { get; } = "UpdatedAt";
        public string ColIsActive { get; } = "IsActive";
        public string ColVersion { get; } = "Version";
        public string ColBatchId { get; } = "BatchId";
        public string ColAwaitsBatchId { get; } = "AwaitsBatchId";
        public string ColContinuationCondition { get; } = "ContinuationCondition";
        public string ColOriginallyScheduledAt { get; } = "OriginallyScheduledAt";

        public string DateTimeColumnType { get; } = "datetime2";

        public IsolationLevel? ClaimIsolationLevel { get; } = null;

        public Task<List<XactJob>> ClaimJobsAsync(DbContext db, string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds, CancellationToken cancellationToken)
        {
            return SqlDialectExtensions.ClaimJobsInOneStatementAsync(db, GetFetchJobsSql(queueName, maxJobs, leaser, leaseDurationInSeconds), cancellationToken);
        }

        public (string Sql, object[] Parameters) GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => ($@"
WITH cte AS (
    SELECT TOP ({{0}}) [{ColId}], [{ColQueue}], [{ColScheduledAt}]
    FROM [{XactJobSchema}].[{XactJobTable}] WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE [{ColScheduledAt}] <= SYSUTCDATETIME()
      AND [{ColQueue}] = {{1}}
      AND ([{ColLeasedUntil}] IS NULL OR [{ColLeasedUntil}] < SYSUTCDATETIME())
    ORDER BY [{ColScheduledAt}]
)
UPDATE target
SET [{ColLeaser}] = {{2}},
    [{ColLeasedUntil}] = DATEADD(SECOND, {{3}}, SYSUTCDATETIME())
OUTPUT inserted.*
FROM [{XactJobSchema}].[{XactJobTable}] AS target
INNER JOIN cte ON -- we need to join on the entire PK
    target.[{ColId}] = cte.[{ColId}]
    AND target.[{ColQueue}] = cte.[{ColQueue}]
    AND target.[{ColScheduledAt}] = cte.[{ColScheduledAt}]
", [maxJobs, queueName ?? QueueNames.Default, leaser, leaseDurationInSeconds]);

        public Task<List<XactJob>> ClaimContinuationsAsync(DbContext db, string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds, DateTime claimedAt, CancellationToken cancellationToken)
        {
            return SqlDialectExtensions.ClaimJobsInOneStatementAsync(db, GetFetchContinuationsSql(queueName, maxJobs, leaser, leaseDurationInSeconds, claimedAt), cancellationToken);
        }

        // The batch's jobs are read without READPAST, so a job that's being retried is waited for instead of skipped.
        // Without snapshot isolation, a reader can miss a row inserted behind it: so a retry is inserted before the failed attempt is deleted.
        public (string Sql, object[] Parameters) GetFetchContinuationsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds, DateTime claimedAt) => ($@"
WITH cte AS (
    SELECT TOP ({{0}}) c.[{ColId}], c.[{ColQueue}], c.[{ColScheduledAt}]
    FROM [{XactJobSchema}].[{XactJobTable}] AS c WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE c.[{ColQueue}] = {{1}}
      AND c.[{ColScheduledAt}] = {{2}}
      AND c.[{ColAwaitsBatchId}] IS NOT NULL
      AND (c.[{ColLeasedUntil}] IS NULL OR c.[{ColLeasedUntil}] < SYSUTCDATETIME())
      AND NOT EXISTS (SELECT 1 FROM [{XactJobSchema}].[{XactJobTable}] AS b WHERE b.[{ColBatchId}] = c.[{ColAwaitsBatchId}])
    ORDER BY c.[{ColId}]
)
UPDATE target
SET [{ColLeaser}] = {{3}},
    [{ColLeasedUntil}] = DATEADD(SECOND, {{4}}, SYSUTCDATETIME()),
    [{ColOriginallyScheduledAt}] = COALESCE(target.[{ColOriginallyScheduledAt}], {{5}})
OUTPUT inserted.*
FROM [{XactJobSchema}].[{XactJobTable}] AS target
INNER JOIN cte ON
    target.[{ColId}] = cte.[{ColId}]
    AND target.[{ColQueue}] = cte.[{ColQueue}]
    AND target.[{ColScheduledAt}] = cte.[{ColScheduledAt}]
", [maxJobs, queueName ?? QueueNames.Default, XactJobBase.AwaitingBatchScheduledAt, leaser, leaseDurationInSeconds, claimedAt]);

        public (string Sql, object[] Parameters) GetExtendLeaseSql(Guid leaser, IReadOnlyCollection<XactJob> jobs, int leaseDurationInSeconds)
        {
            List<object> parameters = [leaseDurationInSeconds, leaser];

            var jobKeys = SqlDialectExtensions.BuildJobKeysPredicate(jobs, parameters, $"[{ColQueue}]", $"[{ColScheduledAt}]", $"[{ColId}]");

            return ($@"
UPDATE [{XactJobSchema}].[{XactJobTable}]
SET [{ColLeasedUntil}] = DATEADD(SECOND, {{0}}, SYSUTCDATETIME())
WHERE [{ColLeaser}] = {{1}}
  AND ({jobKeys})
", parameters.ToArray());
        }

        public (string Sql, object[] Parameters) GetClearLeaseSql(Guid leaser, IReadOnlyCollection<XactJob> jobs)
        {
            List<object> parameters = [leaser];

            var jobKeys = SqlDialectExtensions.BuildJobKeysPredicate(jobs, parameters, $"[{ColQueue}]", $"[{ColScheduledAt}]", $"[{ColId}]");

            return ($@"
UPDATE [{XactJobSchema}].[{XactJobTable}]
SET [{ColLeaser}] = NULL, [{ColLeasedUntil}] = NULL
WHERE [{ColLeaser}] = {{0}}
  AND ({jobKeys})
", parameters.ToArray());
        }

        public async Task AcquireTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            var tableLockName = $"[{XactJobSchema}].[{XactJobPeriodicTable}]";

            var result = await db.Database.ExecuteOutputIntAsync(@$"
EXEC @result = sp_getapplock @Resource = '{tableLockName}', @LockMode = 'Exclusive', @LockTimeout = 30000
", cancellationToken)
                .ConfigureAwait(false);

            if (result < 0) throw new Exception($"Failed to acquire lock (LockName='{tableLockName}', Result={result})");
        }

        public Task ReleaseTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
