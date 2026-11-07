using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace XactJobs.Tests;

/// <summary>
/// <see cref="OriginalScheduleScenarios"/> on SQLite, with the schema from the SQL scripts.
/// </summary>
public sealed class SqliteOriginalScheduleTests : IAsyncLifetime
{
    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly OriginalScheduleScenarios _scenarios;

    public SqliteOriginalScheduleTests()
    {
        var connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

        _memoryConnection = new SqliteConnection(connectionString);
        _scenarios = new OriginalScheduleScenarios(options => options.UseSqlite(connectionString));
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
    public Task OneOffJob_ItsRetriesSeeTheTimeOfTheFirstAttempt() => _scenarios.OneOffJob_ItsRetriesSeeTheTimeOfTheFirstAttempt();

    [Fact]
    public Task PeriodicJob_EachRunSeesItsSlot_AndItsRetriesTheSame() => _scenarios.PeriodicJob_EachRunSeesItsSlot_AndItsRetriesTheSame();

    [Fact]
    public Task Continuation_ItsRetriesSeeTheTimeItsFirstAttemptWasClaimed() => _scenarios.Continuation_ItsRetriesSeeTheTimeItsFirstAttemptWasClaimed();

    [Fact]
    public Task RunNowAndTrigger_KeepTheOriginalTime() => _scenarios.RunNowAndTrigger_KeepTheOriginalTime();

    [Fact]
    public Task DashboardRetry_KeepsTheOriginalTime() => _scenarios.DashboardRetry_KeepsTheOriginalTime();

    [Fact]
    public Task Dashboard_ShowsTheOriginalTimeWhenItDiffers_AndCancelKeepsIt() => _scenarios.Dashboard_ShowsTheOriginalTimeWhenItDiffers_AndCancelKeepsIt();

    [Fact]
    public Task RowWithoutOriginalTime_SeesItsScheduledTime() => _scenarios.RowWithoutOriginalTime_SeesItsScheduledTime();

    [Fact]
    public Task ContextPassedAtEnqueue_IsNotStored() => _scenarios.ContextPassedAtEnqueue_IsNotStored();

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
