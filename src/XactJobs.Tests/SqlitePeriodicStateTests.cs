using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace XactJobs.Tests;

/// <summary>
/// <see cref="PeriodicStateScenarios"/> on SQLite, with the schema from the SQL scripts.
/// </summary>
public sealed class SqlitePeriodicStateTests : IAsyncLifetime
{
    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly PeriodicStateScenarios _scenarios;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    public SqlitePeriodicStateTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
        _scenarios = new PeriodicStateScenarios(options => options.UseSqlite(_connectionString));
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
    public Task EachRun_GetsTheStateThePreviousRunSet() => _scenarios.EachRun_GetsTheStateThePreviousRunSet();

    [Fact]
    public Task FailedAttempt_LeavesTheState_AndItsRetryGetsTheSame() => _scenarios.FailedAttempt_LeavesTheState_AndItsRetryGetsTheSame();

    [Fact]
    public Task RunWithoutSet_KeepsTheState_AndNullClearsIt() => _scenarios.RunWithoutSet_KeepsTheState_AndNullClearsIt();

    [Fact]
    public Task LongestState_RoundTrips_AndALongerOneFailsTheAttempt() => _scenarios.LongestState_RoundTrips_AndALongerOneFailsTheAttempt();

    [Fact]
    public Task OneOffJob_HasNoState_AndCantSetOne() => _scenarios.OneOffJob_HasNoState_AndCantSetOne();

    [Fact]
    public Task DefinitionChange_KeepsTheState() => _scenarios.DefinitionChange_KeepsTheState();

    [Fact]
    public Task Dashboard_ShowsTheState_AndDoesntRetryARunWithOne() => _scenarios.Dashboard_ShowsTheState_AndDoesntRetryARunWithOne();

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
