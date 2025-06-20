using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using XactJobs.SqlDialects;

namespace XactJobs
{
    public interface ISqlDialect
    {
        Guid NewJobId();

        bool HasSchemaSupport { get; }
        string SchemaName { get; }

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

        string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds);

        string GetClearLeaseSql(Guid leaser);

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

                throw new NotSupportedException($"XactJobs does not support provider '{key}'.");
            });
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
