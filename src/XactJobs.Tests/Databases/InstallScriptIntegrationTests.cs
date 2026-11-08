using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using XactJobs.Internal.SqlDialects;
using XactJobs.Tests.Postgres;

namespace XactJobs.Tests.Databases;

// The class names start with the database, so a filter like "XactJobs.Tests.Databases.Oracle" runs all the suites of one database.
// MySQL's schema doesn't depend on the provider: the official one is enough.

[Collection(OfficialMySqlCollection.Name)]
public sealed class OfficialMySqlInstallScriptTests(OfficialMySqlFixture fixture) : InstallScriptIntegrationTests(fixture);

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerInstallScriptTests(SqlServerFixture fixture) : InstallScriptIntegrationTests(fixture);

[Collection(OracleCollection.Name)]
public sealed class OracleInstallScriptTests(OracleFixture fixture) : InstallScriptIntegrationTests(fixture);

[Collection(Postgres16Collection.Name)]
public sealed class Postgres16InstallScriptTests(Postgres16Fixture fixture) : InstallScriptIntegrationTests(fixture);

[Collection(Postgres17Collection.Name)]
public sealed class Postgres17InstallScriptTests(Postgres17Fixture fixture) : InstallScriptIntegrationTests(fixture);

[Collection(Postgres18Collection.Name)]
public sealed class Postgres18InstallScriptTests(Postgres18Fixture fixture) : InstallScriptIntegrationTests(fixture);

/// <summary>
/// The install script (sql/install), for a new installation, creates the same schema as the versioned scripts (sql/v*), which the
/// fixture ran in order: the same columns (types, nullability, defaults, identity), indexes (columns, uniqueness, filters) and keys.
/// The install script's schema is created next to the fixture's under other names, compared, and dropped.
/// </summary>
public abstract class InstallScriptIntegrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        if (DatabaseFixture.IsDockerAvailable)
        {
            await fixture.PrepareForTestAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [DockerFact]
    public async Task InstallScript_CreatesTheSameSchemaAsTheVersionedScripts()
    {
        var database = Database.For(fixture.Dialect);

        var options = new DbContextOptionsBuilder<TestDbContext>();

        fixture.Configure(options, retryOnFailure: false);

        await using var db = new TestDbContext(options.Options);

        var connection = db.Database.GetDbConnection();

        await connection.OpenAsync();

        var installScript = database.Rename(await File.ReadAllTextAsync(GetInstallScript(database.ScriptName)));

        try
        {
            foreach (var statement in database.Statements(installScript))
            {
                await ExecuteAsync(connection, statement);
            }

            var versioned = await database.DescribeAsync(connection, renamed: false);
            var installed = await database.DescribeAsync(connection, renamed: true);

            Assert.NotEmpty(versioned);
            Assert.Equal(string.Join("\n", versioned), string.Join("\n", installed));
        }
        finally
        {
            foreach (var statement in database.DropRenamed)
            {
                await ExecuteAsync(connection, statement);
            }
        }
    }

    internal static string GetInstallScript(string databaseName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "sql", "install")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return Path.Combine(directory.FullName, "sql", "install", $"xact-jobs-{databaseName}.sql");
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// How the install script's schema is put next to the fixture's, and how a schema is described, per database.
    /// </summary>
    private abstract class Database
    {
        public abstract string ScriptName { get; }

        /// <summary>
        /// The install script, creating its objects under the other names.
        /// </summary>
        public abstract string Rename(string script);

        public virtual IEnumerable<string> Statements(string script) => [script];

        public abstract string[] DropRenamed { get; }

        /// <summary>
        /// The schema's description, sorted, with the names as the versioned scripts have them.
        /// </summary>
        public abstract Task<List<string>> DescribeAsync(DbConnection connection, bool renamed);

        public static Database For(object dialect) => dialect switch
        {
            PostgreSqlDialect => new PostgreSql(),
            SqlServerDialect => new SqlServer(),
            MySqlDialect => new MySql(),
            OracleDialect => new Oracle(),
            _ => throw new NotSupportedException(dialect.GetType().Name),
        };

        protected static async Task<List<string>> ReadAsync(DbConnection connection, string sql, Func<DbDataReader, string> read, Action<DbCommand>? prepare = null)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = sql;
            prepare?.Invoke(command);

            await using var reader = await command.ExecuteReaderAsync();

            List<string> rows = [];

            while (await reader.ReadAsync())
            {
                rows.Add(read(reader));
            }

            return rows;
        }
    }

    // another schema
    private sealed class PostgreSql : Database
    {
        public override string ScriptName => "postgresql";

        public override string Rename(string script) => script.Replace("xact_jobs", "install_check");

        public override string[] DropRenamed => ["DROP SCHEMA IF EXISTS install_check CASCADE"];

        public override async Task<List<string>> DescribeAsync(DbConnection connection, bool renamed)
        {
            var schema = renamed ? "install_check" : "xact_jobs";

            var rows = await ReadAsync(connection, $@"
SELECT 'column ' || table_name || '.' || column_name || ' ' || data_type || coalesce('(' || character_maximum_length || ')', '')
       || ' nullable=' || is_nullable || ' default=' || coalesce(column_default, '') || ' identity=' || is_identity
FROM information_schema.columns WHERE table_schema = '{schema}'
UNION ALL
SELECT 'index ' || indexname || ' ' || indexdef FROM pg_indexes WHERE schemaname = '{schema}'
UNION ALL
SELECT 'table ' || c.relname || ' ' || coalesce(array_to_string(c.reloptions, ','), '')
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = '{schema}' AND c.relkind = 'r'", r => r.GetString(0));

            return [.. rows.Select(row => row.Replace("install_check.", "xact_jobs.")).Order(StringComparer.Ordinal)];
        }
    }

    // another schema
    private sealed class SqlServer : Database
    {
        public override string ScriptName => "sqlserver";

        public override string Rename(string script) => script.Replace("XactJobs", "InstallCheck");

        public override string[] DropRenamed =>
        [
            "DROP TABLE IF EXISTS [InstallCheck].[Job], [InstallCheck].[JobHistory], [InstallCheck].[JobPeriodic]",
            "IF SCHEMA_ID(N'InstallCheck') IS NOT NULL DROP SCHEMA [InstallCheck]",
        ];

        public override async Task<List<string>> DescribeAsync(DbConnection connection, bool renamed)
        {
            var schema = renamed ? "InstallCheck" : "XactJobs";

            var rows = await ReadAsync(connection, $@"
SELECT 'column ' + c.TABLE_NAME + '.' + c.COLUMN_NAME + ' ' + c.DATA_TYPE + ISNULL('(' + CAST(c.CHARACTER_MAXIMUM_LENGTH AS varchar(20)) + ')', '')
       + ' precision=' + ISNULL(CAST(c.DATETIME_PRECISION AS varchar(20)), '') + ' nullable=' + c.IS_NULLABLE
       + ' default=' + ISNULL(c.COLUMN_DEFAULT, '')
       + ' identity=' + CAST(COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsIdentity') AS varchar(20))
FROM INFORMATION_SCHEMA.COLUMNS c WHERE c.TABLE_SCHEMA = '{schema}'
UNION ALL
SELECT 'index ' + t.name COLLATE DATABASE_DEFAULT + '.' + i.name COLLATE DATABASE_DEFAULT + ' ' + i.type_desc COLLATE DATABASE_DEFAULT + ' unique=' + CAST(i.is_unique AS varchar(1)) + ' pk=' + CAST(i.is_primary_key AS varchar(1))
       + ' filter=' + ISNULL(i.filter_definition COLLATE DATABASE_DEFAULT, '')
       + ' columns=' + (SELECT STRING_AGG(col.name COLLATE DATABASE_DEFAULT, ',') WITHIN GROUP (ORDER BY ic.key_ordinal)
                        FROM sys.index_columns ic JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
                        WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id)
FROM sys.indexes i JOIN sys.tables t ON t.object_id = i.object_id
WHERE SCHEMA_NAME(t.schema_id) = '{schema}' AND i.type > 0
UNION ALL
SELECT 'default ' + t.name COLLATE DATABASE_DEFAULT + '.' + d.name COLLATE DATABASE_DEFAULT + ' ' + d.definition COLLATE DATABASE_DEFAULT
FROM sys.default_constraints d JOIN sys.tables t ON t.object_id = d.parent_object_id
WHERE SCHEMA_NAME(t.schema_id) = '{schema}'", r => r.GetString(0));

            return [.. rows.Select(row => row.Replace("InstallCheck", "XactJobs")).Order(StringComparer.Ordinal)];
        }
    }

    // other table names (index names belong to their table)
    private sealed class MySql : Database
    {
        public override string ScriptName => "mysql";

        public override string Rename(string script) => script.Replace("`xact_jobs_", "`install_check_");

        public override string[] DropRenamed => ["DROP TABLE IF EXISTS install_check_job, install_check_job_history, install_check_job_periodic"];

        public override async Task<List<string>> DescribeAsync(DbConnection connection, bool renamed)
        {
            var prefix = renamed ? "install_check_" : "xact_jobs_";

            var rows = await ReadAsync(connection, $@"
SELECT CONCAT('column ', SUBSTRING(TABLE_NAME, {prefix.Length + 1}), '.', COLUMN_NAME, ' ', COLUMN_TYPE, ' nullable=', IS_NULLABLE,
              ' default=', IFNULL(COLUMN_DEFAULT, ''), ' charset=', IFNULL(CHARACTER_SET_NAME, ''), ' collation=', IFNULL(COLLATION_NAME, ''),
              ' extra=', EXTRA)
FROM information_schema.columns WHERE TABLE_SCHEMA = DATABASE() AND LEFT(TABLE_NAME, {prefix.Length}) = '{prefix}'
UNION ALL
SELECT CONCAT('index ', SUBSTRING(TABLE_NAME, {prefix.Length + 1}), '.', INDEX_NAME, ' unique=', 1 - NON_UNIQUE,
              ' columns=', GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX))
FROM information_schema.statistics WHERE TABLE_SCHEMA = DATABASE() AND LEFT(TABLE_NAME, {prefix.Length}) = '{prefix}'
GROUP BY TABLE_NAME, INDEX_NAME, NON_UNIQUE
UNION ALL
SELECT CONCAT('table ', SUBSTRING(TABLE_NAME, {prefix.Length + 1}), ' ', ENGINE, ' ', TABLE_COLLATION)
FROM information_schema.tables WHERE TABLE_SCHEMA = DATABASE() AND LEFT(TABLE_NAME, {prefix.Length}) = '{prefix}'", r => r.GetString(0));

            return [.. rows.Order(StringComparer.Ordinal)];
        }
    }

    // other table, index and key names, which all belong to the schema (user)
    private sealed class Oracle : Database
    {
        private const string Prefix = "ICHK_";

        public override string ScriptName => "oracle";

        public override string Rename(string script) => script
            .Replace("\"XACT_JOBS_", $"\"{Prefix}XACT_JOBS_")
            .Replace("\"PK_", $"\"{Prefix}PK_")
            .Replace("\"IX_", $"\"{Prefix}IX_");

        // Oracle runs one statement at a time, like the fixture does with the versioned scripts
        public override IEnumerable<string> Statements(string script) => script
            .Split(';')
            .Select(statement => string.Join("\n", statement.Split('\n').Select(line => line.Trim()).Where(line => !line.StartsWith("--"))).Trim())
            .Where(statement => statement.Length > 0);

        public override string[] DropRenamed =>
        [
            $"BEGIN FOR t IN (SELECT TABLE_NAME FROM USER_TABLES WHERE SUBSTR(TABLE_NAME, 1, {Prefix.Length}) = '{Prefix}') LOOP EXECUTE IMMEDIATE 'DROP TABLE \"' || t.TABLE_NAME || '\" PURGE'; END LOOP; END;",
        ];

        public override async Task<List<string>> DescribeAsync(DbConnection connection, bool renamed)
        {
            var table = renamed ? $"SUBSTR(TABLE_NAME, 1, {Prefix.Length + 10}) = '{Prefix}XACT_JOBS_'" : "SUBSTR(TABLE_NAME, 1, 10) = 'XACT_JOBS_'";

            // the identity's default names a sequence of the table's own
            var columns = await ReadAsync(connection, $@"
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHAR_LENGTH, DATA_PRECISION, DATA_SCALE, NULLABLE, IDENTITY_COLUMN, DATA_DEFAULT
FROM USER_TAB_COLUMNS WHERE {table}", r =>
                $"column {r["TABLE_NAME"]}.{r["COLUMN_NAME"]} {r["DATA_TYPE"]} length={r["CHAR_LENGTH"]} precision={r["DATA_PRECISION"]} scale={r["DATA_SCALE"]}" +
                $" nullable={r["NULLABLE"]} identity={r["IDENTITY_COLUMN"]} default={((string)r["IDENTITY_COLUMN"] == "YES" ? "" : Convert.ToString(r["DATA_DEFAULT"])?.Trim())}",
                command => ((OracleCommand)command).InitialLONGFetchSize = -1);

            // not the LOB indexes, which Oracle names on its own
            var indexes = await ReadAsync(connection, $@"
SELECT 'index ' || i.INDEX_NAME || ' ' || i.TABLE_NAME || ' ' || i.UNIQUENESS || ' ' || LISTAGG(c.COLUMN_NAME, ',') WITHIN GROUP (ORDER BY c.COLUMN_POSITION)
FROM USER_INDEXES i JOIN USER_IND_COLUMNS c ON c.INDEX_NAME = i.INDEX_NAME
WHERE i.INDEX_TYPE <> 'LOB' AND SUBSTR(i.TABLE_NAME, 1, {(renamed ? Prefix.Length + 10 : 10)}) = '{(renamed ? Prefix : "")}XACT_JOBS_'
GROUP BY i.INDEX_NAME, i.TABLE_NAME, i.UNIQUENESS", r => r.GetString(0));

            // the keys, not the NOT NULL checks, which Oracle names on its own
            var keys = await ReadAsync(connection, $@"
SELECT 'key ' || CONSTRAINT_NAME || ' ' || TABLE_NAME || ' ' || CONSTRAINT_TYPE
FROM USER_CONSTRAINTS WHERE CONSTRAINT_TYPE IN ('P', 'U') AND {table}", r => r.GetString(0));

            return [.. columns.Concat(indexes).Concat(keys).Select(row => row.Replace(Prefix, "")).Order(StringComparer.Ordinal)];
        }
    }
}
