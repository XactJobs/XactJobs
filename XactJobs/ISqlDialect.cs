using System.Collections.Concurrent;
using XactJobs.SqlDialects;

namespace XactJobs
{
    public interface ISqlDialect
    {
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

        Guid NewJobId();
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
                if (key.EndsWith(".oracle")) return new OracleDialect();

                throw new NotSupportedException($"XactJobs does not support provider '{key}'.");
            });
        }
    }
}
