using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace XactJobs.Tests;

/// <summary>
/// <see cref="SaveCheckScenarios"/> on SQLite, with the schema from the SQL scripts.
/// </summary>
public sealed class SqliteSaveCheckTests : IAsyncLifetime
{
    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly SaveCheckScenarios _scenarios;

    public SqliteSaveCheckTests()
    {
        var connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

        _memoryConnection = new SqliteConnection(connectionString);
        _scenarios = new SaveCheckScenarios(options => options.UseSqlite(connectionString));
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

    public async Task DisposeAsync()
    {
        await _scenarios.DisposeAsync();
        await _memoryConnection.DisposeAsync();
    }

    [Fact]
    public Task SaveWithoutTransaction_Warns_OncePerJobMethod() => _scenarios.SaveWithoutTransaction_Warns_OncePerJobMethod();

    [Fact]
    public Task SaveInsideTheJobsOwnTransaction_DoesntWarn() => _scenarios.SaveInsideTheJobsOwnTransaction_DoesntWarn();

    [Fact]
    public Task JobThatDoesntSave_DoesntWarn() => _scenarios.JobThatDoesntSave_DoesntWarn();

    [Fact]
    public Task ThrowMode_JobsThatSaveTheRightWay_CompleteOnTheFirstAttempt() => _scenarios.ThrowMode_JobsThatSaveTheRightWay_CompleteOnTheFirstAttempt();

    [Fact]
    public Task ThrowMode_SaveWithoutTransaction_Fails_AndIsRetried() => _scenarios.ThrowMode_SaveWithoutTransaction_Fails_AndIsRetried();

    [Fact]
    public Task IgnoreMode_SaveWithoutTransaction_DoesntWarn() => _scenarios.IgnoreMode_SaveWithoutTransaction_DoesntWarn();

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
