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
