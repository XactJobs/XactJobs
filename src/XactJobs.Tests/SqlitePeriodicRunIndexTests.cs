using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace XactJobs.Tests;

/// <summary>
/// <see cref="PeriodicRunIndexScenarios"/> on SQLite, with the schema from the SQL scripts.
/// </summary>
public sealed class SqlitePeriodicRunIndexTests : IAsyncLifetime
{
    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly PeriodicRunIndexScenarios _scenarios;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    public SqlitePeriodicRunIndexTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
        _scenarios = new PeriodicRunIndexScenarios(options => options.UseSqlite(_connectionString));
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
    public Task SecondRunOfAVersion_IsRefused() => _scenarios.SecondRunOfAVersion_IsRefused();

    [Fact]
    public Task Dashboard_TriggerAndResume_WhenARunIsAddedAtTheSameTime_AreConflicts() => _scenarios.Dashboard_TriggerAndResume_WhenARunIsAddedAtTheSameTime_AreConflicts();

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
