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
        public string CheckConstraintPrefix { get; } = "CHK";
        public string UniquePrefix { get; } = "UQ";

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

        public string? GetAcquireLeaseSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeaser} = HEXTORAW('{leaser:N}'),
    {ColLeasedUntil} = SYS_EXTRACT_UTC(systimestamp) + INTERVAL '{leaseDurationInSeconds}' SECOND
WHERE {ColScheduledAt} <= SYS_EXTRACT_UTC(systimestamp)
  AND {ColQueue} = '{queueName ?? QueueNames.Default}'
  AND ({ColLeasedUntil} IS NULL OR {ColLeasedUntil} < SYS_EXTRACT_UTC(systimestamp))
  AND ROWNUM <= {maxJobs}
";

        public string GetFetchJobsSql(string? queueName, int maxJobs, Guid leaser, int leaseDurationInSeconds) => $@"
SELECT *
FROM {XactJobSchema}_{XactJobTable}
WHERE {ColLeaser} = HEXTORAW('{leaser:N}')
  AND {ColLeasedUntil} >= SYS_EXTRACT_UTC(systimestamp)
  AND ROWNUM <= {maxJobs}
";

        public string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds) => $@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeasedUntil} = SYS_EXTRACT_UTC(systimestamp) + INTERVAL '{leaseDurationInSeconds}' SECOND
WHERE {ColLeaser} = HEXTORAW('{leaser:N}')
";

        public string GetClearLeaseSql(Guid leaser) => $@"
UPDATE {XactJobSchema}_{XactJobTable}
SET {ColLeaser} = NULL, {ColLeasedUntil} = NULL
WHERE {ColLeaser} = HEXTORAW('{leaser:N}')
";

        public string GetPeriodicJobCheckConstraintSql() => $@"
({ColPeriodicJobId} IS NULL AND {ColPeriodicJobVersion} IS NULL)
    OR ({ColPeriodicJobId} IS NOT NULL AND {ColPeriodicJobVersion} IS NOT NULL)";

        public string GetPeriodicJobUniqueIndexFilterSql() => $"{ColPeriodicJobId} IS NOT NULL";

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
