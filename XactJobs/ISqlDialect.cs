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
        string? GetAcquireLeaseSql(int maxJobs, Guid leaser, int leaseDurationInSeconds);

        string GetFetchJobsSql(int maxJobs, Guid leaser, int leaseDurationInSeconds);

        string GetExtendLeaseSql(Guid leaser, int leaseDurationInSeconds);
    }

    internal static class SqlDialectExtensions
    {
        public static ISqlDialect ToSqlDialect(this string? providerName)
        {
            providerName = providerName?.ToLowerInvariant() ?? "";

            if (providerName.EndsWith(".sqlserver")) return new MsSqlDialect();
            if (providerName.EndsWith(".postgresql")) return new PostgresDialect();
            if (providerName.EndsWith(".mysql")) return new MySqlDialect();
            if (providerName.EndsWith(".oracle")) return new OracleDialect();

            throw new NotSupportedException($"XactJobs does not support provider '{providerName}'.");
        }
    }
}
