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
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    public SqliteOriginalScheduleTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
        _scenarios = new OriginalScheduleScenarios(options => options.UseSqlite(_connectionString));
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
    public Task Continuation_RunAgainAfterItsLeaseExpired_SeesTheTimeOfTheFirstClaim() => _scenarios.Continuation_RunAgainAfterItsLeaseExpired_SeesTheTimeOfTheFirstClaim();

    [Fact]
    public Task RegularAndPeriodicClaims_DontSetTheColumn() => _scenarios.RegularAndPeriodicClaims_DontSetTheColumn();

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

    /// <summary>
    /// The claim saves the claim time as the continuation's original time, returns it (SQLite returns the rows after the lease),
    /// and a second claim, after the lease expired, keeps it.
    /// </summary>
    [Fact]
    public async Task Claim_SavesTheClaimTime_AndASecondClaimAfterTheLeaseExpiredKeepsIt()
    {
        var dialect = new XactJobs.Internal.SqlDialects.SqliteDialect();

        var continuation = new XactJob(0, XactJobBase.AwaitingBatchScheduledAt, "T", "M()", "[]", "q")
        {
            AwaitsBatchId = Guid.NewGuid(),
            ContinuationCondition = XactJobContinuation.OnFinish
        };

        await using (var db = CreateDb())
        {
            db.Jobs.Add(continuation);

            await db.SaveChangesAsync();
        }

        var claimedA = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234560);

        await using (var db = CreateDb())
        {
            var first = Assert.Single(await dialect.ClaimContinuationsAsync(db, "q", 10, Guid.NewGuid(), 60, claimedA, CancellationToken.None));

            Assert.Equal(claimedA, first.OriginallyScheduledAt);

            await db.Jobs.ExecuteUpdateAsync(s => s.SetProperty(j => j.LeasedUntil, DateTime.UtcNow.AddMinutes(-1)));
        }

        await using (var db = CreateDb())
        {
            var second = Assert.Single(await dialect.ClaimContinuationsAsync(db, "q", 10, Guid.NewGuid(), 60, claimedA.AddSeconds(5), CancellationToken.None));

            Assert.Equal(claimedA, second.OriginallyScheduledAt);
            Assert.Equal(claimedA, await db.Jobs.AsNoTracking().Select(j => j.OriginallyScheduledAt).SingleAsync());
        }
    }

    private TestDbContext CreateDb() => new(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connectionString).Options);

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
