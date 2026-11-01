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

        string DateTimeColumnType { get; }

        /// <summary>
        /// Optional, if a database does not support update returning (MySQL).
        /// If a database supports update returning, it should return null here
        /// </summary>
        /// <param name="leaser"></param>
        /// <param name="maxJobs"></param>
        /// <returns></returns>
        string? GetAcquireLeaseSql(string? queue, int maxJobs, Guid leaser, int leaseDurationInSeconds);

        string GetFetchJobsSql(string? queue, int maxJobs, Guid leaser, int leaseDurationInSeconds);

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
        private static readonly ConcurrentDictionary<string, ISqlDialect> _cachedDialects = new();

        public static ISqlDialect ToSqlDialect(this string? providerName)
        {
            return _cachedDialects.GetOrAdd(providerName ?? "", key =>
            {
                key = key.ToLowerInvariant();

                if (key.EndsWith(".sqlserver")) return new SqlServerDialect();
                if (key.EndsWith(".postgresql")) return new PostgreSqlDialect();
                if (key.EndsWith(".mysql")) return new MySqlDialect();
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
