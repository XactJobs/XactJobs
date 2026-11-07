using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace XactJobs.Tests;

/// <summary>
/// <see cref="QueryPlanScenarios"/> on SQLite, with the schema from the SQL scripts.
/// </summary>
public sealed class SqliteQueryPlanTests : IAsyncLifetime
{
    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly QueryPlanScenarios _scenarios;

    public SqliteQueryPlanTests()
    {
        var connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

        _memoryConnection = new SqliteConnection(connectionString);
        _scenarios = new QueryPlanScenarios(options => options.UseSqlite(connectionString));
    }

    public async Task InitializeAsync()
    {
        await _memoryConnection.OpenAsync();

        foreach (var script in GetSqliteMigrationScripts())
        {
            await using var command = _memoryConnection.CreateCommand();

            command.CommandText = await File.ReadAllTextAsync(script);

            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync() => await _memoryConnection.DisposeAsync();

    [Fact]
    public Task DashboardJobLookups_UseThePrimaryKey() => _scenarios.DashboardJobLookups_UseThePrimaryKey();

    [Fact]
    public Task PeriodicJobQueries_UseThePeriodicJobIndex() => _scenarios.PeriodicJobQueries_UseThePeriodicJobIndex();

    private static IEnumerable<string> GetSqliteMigrationScripts()
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
