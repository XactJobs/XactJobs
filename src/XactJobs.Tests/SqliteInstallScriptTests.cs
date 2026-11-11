using Microsoft.Data.Sqlite;
using XactJobs.Tests.Databases;

namespace XactJobs.Tests;

/// <summary>
/// The install script (sql/install), for a new installation, creates the same schema as the versioned scripts (sql/v*) run in
/// order: the same columns, indexes (with the text of their filters) and keys. Each in an in-memory database of its own.
/// </summary>
public sealed class SqliteInstallScriptTests
{
    [Fact]
    public async Task InstallScript_CreatesTheSameSchemaAsTheVersionedScripts()
    {
        var versioned = await DescribeAsync(GetVersionedScripts());
        var installed = await DescribeAsync([InstallScriptIntegrationTests.GetInstallScript("sqlite")]);

        Assert.NotEmpty(versioned);
        Assert.Equal(string.Join("\n", versioned), string.Join("\n", installed));
    }

    /// <summary>
    /// v0.6.0 copies the history into a table with the new primary key (the id and the time it was processed): the rows are kept,
    /// and a job can then have a row per attempt.
    /// </summary>
    [Fact]
    public async Task V060_KeepsTheHistory_AndAllowsARowPerAttempt()
    {
        var scripts = GetVersionedScripts().ToList();
        var v060 = Assert.Single(scripts, s => s.EndsWith("v0.6.0.sql"));

        await using var connection = new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        foreach (var script in scripts.TakeWhile(s => s != v060))
        {
            await ExecuteAsync(connection, await File.ReadAllTextAsync(script));
        }

        const string insert = @"
INSERT INTO ""xact_jobs_job_history"" (""id"", ""processed_at"", ""status"", ""scheduled_at"", ""type_name"", ""method_name"", ""method_args"", ""queue"", ""error_count"", ""batch_id"")
VALUES (42, '{0}', 2, '2026-01-01 00:00:00', 'T', 'M()', '[]', 'q', 1, 'b')";

        await ExecuteAsync(connection, string.Format(insert, "2026-01-01 00:00:01"));

        await ExecuteAsync(connection, await File.ReadAllTextAsync(v060));

        // the second attempt of the same job
        await ExecuteAsync(connection, string.Format(insert, "2026-01-01 00:00:02"));

        await using var command = connection.CreateCommand();

        command.CommandText = @"SELECT COUNT(*) FROM ""xact_jobs_job_history"" WHERE ""id"" = 42 AND ""batch_id"" = 'b' AND ""error_count"" = 1";

        Assert.Equal(2L, await command.ExecuteScalarAsync());
    }

    private const string InsertPeriodicRun = @"
INSERT INTO ""xact_jobs_job"" (""scheduled_at"", ""queue"", ""type_name"", ""method_name"", ""method_args"", ""periodic_job_id"", ""error_count"", ""periodic_job_version"")
VALUES ('{0}', 'q', 'T', 'M()', '[]', 'p', 0, {1})";

    [Fact]
    public async Task V060_GivesTheRunsWithoutAVersionTheFirst()
    {
        await using var connection = await OpenBeforeV060Async();

        await ExecuteAsync(connection, string.Format(InsertPeriodicRun, "2026-01-01 00:00:00", "NULL"));

        await ExecuteAsync(connection, await File.ReadAllTextAsync(GetV060Script()));

        await using var command = connection.CreateCommand();

        command.CommandText = @"SELECT ""periodic_job_version"" FROM ""xact_jobs_job"" WHERE ""periodic_job_id"" = 'p'";

        Assert.Equal(1L, await command.ExecuteScalarAsync());

        // a second run of that version is refused
        var ex = await Assert.ThrowsAsync<SqliteException>(() => ExecuteAsync(connection, string.Format(InsertPeriodicRun, "2026-01-01 00:01:00", "1")));

        Assert.True(ex.IsUniqueKeyViolation());
    }

    [Fact]
    public async Task V060_FailsOnTwoRunsOfAVersion_BeforeChangingTheOtherTables()
    {
        await using var connection = await OpenBeforeV060Async();

        // the same version: with and without it
        await ExecuteAsync(connection, string.Format(InsertPeriodicRun, "2026-01-01 00:00:00", "1"));
        await ExecuteAsync(connection, string.Format(InsertPeriodicRun, "2026-01-01 00:01:00", "NULL"));

        var ex = await Assert.ThrowsAsync<SqliteException>(async () => await ExecuteAsync(connection, await File.ReadAllTextAsync(GetV060Script())));

        Assert.True(ex.IsUniqueKeyViolation());

        // the history doesn't have the new columns yet: the script can run again once the extra run is deleted
        await using var command = connection.CreateCommand();

        command.CommandText = @"SELECT COUNT(*) FROM pragma_table_info('xact_jobs_job_history') WHERE name = 'state'";

        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    private static string GetV060Script() => Assert.Single(GetVersionedScripts(), s => s.EndsWith("v0.6.0.sql"));

    private static async Task<SqliteConnection> OpenBeforeV060Async()
    {
        var connection = new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        foreach (var script in GetVersionedScripts().TakeWhile(s => !s.EndsWith("v0.6.0.sql")))
        {
            await ExecuteAsync(connection, await File.ReadAllTextAsync(script));
        }

        return connection;
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> DescribeAsync(IEnumerable<string> scripts)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");

        await connection.OpenAsync();

        foreach (var script in scripts)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = await File.ReadAllTextAsync(script);

            await command.ExecuteNonQueryAsync();
        }

        await using var describe = connection.CreateCommand();

        describe.CommandText = @"
SELECT 'column ' || m.name || '.' || p.name || ' ' || p.type || ' notnull=' || p.""notnull"" || ' default=' || IFNULL(p.dflt_value, '') || ' pk=' || p.pk
FROM sqlite_master m JOIN pragma_table_info(m.name) p
WHERE m.type = 'table'
UNION ALL
SELECT 'index ' || m.name || ' ' || m.tbl_name || ' ' || IFNULL(m.sql, '')
FROM sqlite_master m
WHERE m.type = 'index'
UNION ALL
SELECT 'table ' || m.name || ' autoincrement=' || (m.sql LIKE '%AUTOINCREMENT%')
FROM sqlite_master m
WHERE m.type = 'table'";

        await using var reader = await describe.ExecuteReaderAsync();

        List<string> rows = [];

        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return [.. rows.Order(StringComparer.Ordinal)];
    }

    private static IEnumerable<string> GetVersionedScripts()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "sql", "v0.1.0")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return new DirectoryInfo(Path.Combine(directory.FullName, "sql"))
            .GetDirectories("v*")
            .OrderBy(d => Version.Parse(d.Name[1..]))
            .Select(d => Path.Combine(d.FullName, $"xact-jobs-sqlite-{d.Name}.sql"))
            .Where(File.Exists);
    }
}
