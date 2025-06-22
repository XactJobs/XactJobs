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
    public class SqlServerDialect : ISqlDialect
    {
        public bool HasSchemaSupport { get; } = true;

        public string XactJobSchema { get; } = "XactJobs";
        public string XactJobTable { get; } = "Job";
        public string XactJobHistoryTable { get; } = "JobHistory";
        public string XactJobPeriodicTable { get; } = "JobPeriodic";

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
        public string ColErrorCount { get; } = "ErrorCount";
        public string ColErrorTime { get; } = "ErrorTime";
        public string ColErrorMessage { get; } = "ErrorMessage";
        public string ColErrorStackTrace { get; } = "ErrorStackTrace";
        public string ColCronExpression { get; } = "CronExpression";
        public string ColUpdatedAt { get; } = "UpdatedAt";
        public string ColIsActive { get; } = "IsActive";

        public string DateTimeColumnType { get; } = "datetime2";

        public string? GetAcquireLeaseSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => null;

        public string GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
WITH cte AS (
    SELECT TOP ({maxJobs}) [{ColId}], [{ColQueue}], [{ColScheduledAt}]
    FROM [{XactJobSchema}].[{XactJobTable}] WITH (UPDLOCK, READPAST, ROWLOCK)
    WHERE [{ColScheduledAt}] <= SYSUTCDATETIME()
      AND [{ColQueue}] = '{queueName ?? QueueNames.Default}'
      AND ([{ColLeasedUntil}] IS NULL OR [{ColLeasedUntil}] < SYSUTCDATETIME())
    ORDER BY [{ColScheduledAt}]
)
UPDATE target
SET [{ColLeaser}] = CAST('{leaser}' AS UNIQUEIDENTIFIER),
    [{ColLeasedUntil}] = DATEADD(SECOND, {leaseDurationInSeconds}, SYSUTCDATETIME())
OUTPUT inserted.*
FROM [{XactJobSchema}].[{XactJobTable}] AS target
INNER JOIN cte ON -- we need to join on the entire PK
    target.[{ColId}] = cte.[{ColId}]
    AND target.[{ColQueue}] = cte.[{ColQueue}]
    AND target.[{ColScheduledAt}] = cte.[{ColScheduledAt}]
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE [{XactJobSchema}].[{XactJobTable}]
SET [{ColLeasedUntil}] = DATEADD(SECOND, {leaseDurationInSeconds}, SYSUTCDATETIME())
WHERE [{ColLeaser}] = CAST('{leaser}' AS UNIQUEIDENTIFIER)
";

        public string GetClearLeaseSql(Guid leaser) => $@"
UPDATE [{XactJobSchema}].[{XactJobTable}]
SET [{ColLeaser}] = NULL, [{ColLeasedUntil}] = NULL
WHERE [{ColLeaser}] = CAST('{leaser}' AS UNIQUEIDENTIFIER)
";

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
