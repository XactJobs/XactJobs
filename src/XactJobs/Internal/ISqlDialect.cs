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
using System.Collections.Concurrent;
using System.Data;

using XactJobs.Internal.SqlDialects;

namespace XactJobs.Internal
{
    internal interface ISqlDialect
    {
        bool HasSchemaSupport { get; }

        string XactJobSchema { get; }
        string XactJobTable { get; }
        string XactJobHistoryTable { get; }
        string XactJobPeriodicTable { get; }

        string PrimaryKeyPrefix { get; }
        string IndexPrefix { get; }

        string ColId { get; }
        string ColCreatedAt { get; }
        string ColScheduledAt { get; }
        string ColProcessedAt { get; }
        string ColLeasedUntil { get; }
        string ColLeaser { get; }
        string ColTypeName { get; }
        string ColMethodName { get; }
        string ColMethodArgs { get; }
        string ColStatus { get; }
        string ColQueue { get; }
        string ColPeriodicJobId { get; }
        string ColPeriodicJobVersion { get; }
        string ColErrorCount { get; }
        string ColErrorTime { get; }
        string ColErrorMessage { get; }
        string ColErrorStackTrace { get; }
        string ColCronExpression { get; }
        string ColUpdatedAt { get; }
        string ColIsActive { get; }
        string ColVersion { get; }
        string ColBatchId { get; }
        string ColAwaitsBatchId { get; }
        string ColContinuationCondition { get; }
        string ColOriginallyScheduledAt { get; }

        string DateTimeColumnType { get; }

        /// <summary>
        /// The isolation level of the transaction that locks and leases the claimed jobs, for the dialects that claim in a transaction
        /// (<see cref="SqlDialectExtensions.ClaimJobsWithLockAsync"/>), or null for the database's default.
        /// Only the claim uses it: running the jobs and extending or clearing their leases don't.
        /// </summary>
        IsolationLevel? ClaimIsolationLevel { get; }

        /// <summary>
        /// Leases up to <paramref name="maxJobs"/> due jobs from the queue and returns them.
        /// </summary>
        Task<List<XactJob>> ClaimJobsAsync(DbContext db, string? queue, int maxJobs, Guid leaser, int leaseDurationInSeconds, CancellationToken cancellationToken);

        /// <summary>
        /// Leases up to <paramref name="maxJobs"/> continuations of the queue that wait for a batch that has no jobs left, and returns them.
        /// The statement that leases them also sets their original time to <paramref name="claimedAt"/>, if it isn't set yet: a second run
        /// of the first attempt, after its lease expired, gets the time of the first claim (see <see cref="XactJobBase.GetOriginallyScheduledAt"/>).
        /// <para>
        /// The check for the batch's jobs must not skip locked rows (no SKIP LOCKED or READPAST in it): a job that is being retried
        /// is replaced in a transaction, and skipping its rows would make the batch look finished.
        /// </para>
        /// </summary>
        Task<List<XactJob>> ClaimContinuationsAsync(DbContext db, string? queue, int maxJobs, Guid leaser, int leaseDurationInSeconds, DateTime claimedAt, CancellationToken cancellationToken);

        /// <summary>
        /// Extends the lease of the given jobs, by primary key, if they are still leased by <paramref name="leaser"/>.
        /// The SQL uses {n} placeholders, as expected by ExecuteSqlRaw.
        /// </summary>
        (string Sql, object[] Parameters) GetExtendLeaseSql(Guid leaser, IReadOnlyCollection<XactJob> jobs, int leaseDurationInSeconds);

        /// <summary>
        /// Clears the lease of the given jobs, by primary key, if they are still leased by <paramref name="leaser"/>.
        /// The SQL uses {n} placeholders, as expected by ExecuteSqlRaw.
        /// </summary>
        (string Sql, object[] Parameters) GetClearLeaseSql(Guid leaser, IReadOnlyCollection<XactJob> jobs);

        Task AcquireTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken);

        Task ReleaseTableLockAsync(DbContext db, string tableSchema, string tableName, CancellationToken cancellationToken);
    }

    internal static class SqlDialectExtensions
    {
        // keeps a statement within the parameter limits of all dialects (SQL Server allows 2100, at 3 per job)
        public const int MaxJobsPerStatement = 500;

        private static readonly ConcurrentDictionary<string, ISqlDialect> _cachedDialects = new();

        /// <summary>
        /// For databases that can update rows and return them in one statement.
        /// </summary>
        public static Task<List<XactJob>> ClaimJobsInOneStatementAsync(DbContext db, (string Sql, object[] Parameters) claimJobs, CancellationToken cancellationToken)
        {
            return db.Set<XactJob>().FromSqlRaw(claimJobs.Sql, claimJobs.Parameters)
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }

        /// <summary>
        /// For databases that can't return updated rows (MySQL): locks the rows with a select,
        /// then leases them by primary key, in one transaction, at <paramref name="isolationLevel"/> if it's set.
        /// </summary>
        public static Task<List<XactJob>> ClaimJobsWithLockAsync(DbContext db,
                                                                 IsolationLevel? isolationLevel,
                                                                 (string Sql, object[] Parameters) lockJobs,
                                                                 Func<XactJob[], (string Sql, object[] Parameters)> getLeaseJobsSql,
                                                                 CancellationToken cancellationToken)
        {
            // a retrying execution strategy, if the app enabled one, only allows explicit transactions inside of it
            return db.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                using var transaction = isolationLevel is { } level
                    ? await db.Database.BeginTransactionAsync(level, token).ConfigureAwait(false)
                    : await db.Database.BeginTransactionAsync(token).ConfigureAwait(false);

                var jobs = await db.Set<XactJob>().FromSqlRaw(lockJobs.Sql, lockJobs.Parameters)
                    .AsNoTracking()
                    .ToListAsync(token)
                    .ConfigureAwait(false);

                foreach (var chunk in jobs.Chunk(MaxJobsPerStatement))
                {
                    var (sql, parameters) = getLeaseJobsSql(chunk);

                    await db.Database.ExecuteSqlRawAsync(sql, parameters, token)
                        .ConfigureAwait(false);
                }

                await transaction.CommitAsync(token)
                    .ConfigureAwait(false);

                return jobs;
            }, cancellationToken);
        }

        public static ISqlDialect ToSqlDialect(this string? providerName)
        {
            return _cachedDialects.GetOrAdd(providerName ?? "", key =>
            {
                key = key.ToLowerInvariant();

                if (key.EndsWith(".sqlserver")) return new SqlServerDialect();
                if (key.EndsWith(".postgresql")) return new PostgreSqlDialect();
                if (key.EndsWith(".mysql")) return new MySqlDialect(); // Pomelo.EntityFrameworkCore.MySql
                if (key.StartsWith("mysql.")) return new MySqlDialect(); // MySql.EntityFrameworkCore
                if (key.StartsWith("oracle.")) return new OracleDialect();
                if (key.EndsWith(".sqlite")) return new SqliteDialect();

                throw new NotSupportedException($"XactJobs does not support provider '{key}'.");
            });
        }

        /// <summary>
        /// Builds "(queue = {n} AND scheduled_at = {n+1} AND id = {n+2}) OR ..." and appends the values to <paramref name="parameters"/>.
        /// </summary>
        public static string BuildJobKeysPredicate(IReadOnlyCollection<XactJob> jobs, List<object> parameters, string colQueue, string colScheduledAt, string colId)
        {
            var rows = new List<string>(jobs.Count);

            foreach (var job in jobs)
            {
                var i = parameters.Count;

                parameters.Add(job.Queue);
                parameters.Add(job.ScheduledAt);
                parameters.Add(job.Id);

                rows.Add($"({colQueue} = {{{i}}} AND {colScheduledAt} = {{{i + 1}}} AND {colId} = {{{i + 2}}})");
            }

            return string.Join(" OR ", rows);
        }

        public static bool IsUniqueKeyViolation(this Exception ex)
        {
            if (ex == null) return false;

            // find inner most
            while (ex.InnerException != null)
            {
                ex = ex.InnerException;
            }

            var exType = ex.GetType();

            var typeName = exType.FullName;

            switch (typeName)
            {
                case "Npgsql.PostgresException":
                    return GetPropertyValue<string>(ex, "SqlState") == "23505";

                case "Microsoft.Data.SqlClient.SqlException":
                case "System.Data.SqlClient.SqlException": // for older drivers
                    {
                        var number = GetPropertyValue<int>(ex, "Number");
                        return number == 2627 || number == 2601;
                    }

                case "Oracle.ManagedDataAccess.Client.OracleException":
                    return GetPropertyValue<int>(ex, "Number") == 1;

                case "MySqlConnector.MySqlException":
                case "MySql.Data.MySqlClient.MySqlException": // for older official lib
                    return GetPropertyValue<int>(ex, "Number") == 1062;

                default:
                    return false;
            }
        }

        private static T? GetPropertyValue<T>(object obj, string propertyName)
        {
            var prop = obj.GetType().GetProperty(propertyName);
            if (prop != null && prop.CanRead)
            {
                return (T?)prop.GetValue(obj);
            }
            return default;
        }
    }
}
