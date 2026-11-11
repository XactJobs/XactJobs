// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

namespace XactJobs
{
    public static class DbExceptionExtensions
    {
        /// <summary>
        /// Whether the database refused a row because of a unique index or key, e.g. a second pending run of a periodic job (the job
        /// table has one per periodic job and version): look for it in the <c>DbUpdateException</c> of a <c>SaveChanges</c>.
        /// It knows the exceptions of Npgsql, SqlClient, ODP.NET, MySqlConnector, MySql.Data and Microsoft.Data.Sqlite.
        /// </summary>
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

                case "Microsoft.Data.Sqlite.SqliteException":
                    {
                        // SQLITE_CONSTRAINT_UNIQUE, SQLITE_CONSTRAINT_PRIMARYKEY
                        var code = GetPropertyValue<int>(ex, "SqliteExtendedErrorCode");
                        return code == 2067 || code == 1555;
                    }

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
