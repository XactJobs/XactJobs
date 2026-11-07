using XactJobs.Tests.Postgres;

namespace XactJobs.Tests.Databases;

// The class names start with the database, so a filter like "XactJobs.Tests.Databases.Oracle" runs all the suites of one database.

[Collection(OfficialMySqlCollection.Name)]
public sealed class OfficialMySqlOriginalScheduleTests(OfficialMySqlFixture fixture) : OriginalScheduleIntegrationTests(fixture);

#if !NET10_0_OR_GREATER // Pomelo doesn't support EF Core 10 yet
[Collection(PomeloMySqlCollection.Name)]
public sealed class PomeloMySqlOriginalScheduleTests(PomeloMySqlFixture fixture) : OriginalScheduleIntegrationTests(fixture);
#endif

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerOriginalScheduleTests(SqlServerFixture fixture) : OriginalScheduleIntegrationTests(fixture);

[Collection(OracleCollection.Name)]
public sealed class OracleOriginalScheduleTests(OracleFixture fixture) : OriginalScheduleIntegrationTests(fixture);

[Collection(PostgresCollection.Name)]
public sealed class PostgresOriginalScheduleTests(PostgresFixture fixture) : OriginalScheduleIntegrationTests(fixture);

/// <summary>
/// <see cref="OriginalScheduleScenarios"/> on every database, with the schema from the SQL scripts.
/// </summary>
public abstract class OriginalScheduleIntegrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly OriginalScheduleScenarios _scenarios = new(options => fixture.Configure(options, retryOnFailure: false));

    public async Task InitializeAsync()
    {
        if (DatabaseFixture.IsDockerAvailable)
        {
            await fixture.PrepareForTestAsync();
        }
    }

    public async Task DisposeAsync() => await _scenarios.DisposeAsync();

    [DockerFact]
    public Task OneOffJob_ItsRetriesSeeTheTimeOfTheFirstAttempt() => _scenarios.OneOffJob_ItsRetriesSeeTheTimeOfTheFirstAttempt();

    [DockerFact]
    public Task PeriodicJob_EachRunSeesItsSlot_AndItsRetriesTheSame() => _scenarios.PeriodicJob_EachRunSeesItsSlot_AndItsRetriesTheSame();

    [DockerFact]
    public Task Continuation_ItsRetriesSeeTheTimeItsFirstAttemptWasClaimed() => _scenarios.Continuation_ItsRetriesSeeTheTimeItsFirstAttemptWasClaimed();

    [DockerFact]
    public Task RunNowAndTrigger_KeepTheOriginalTime() => _scenarios.RunNowAndTrigger_KeepTheOriginalTime();

    [DockerFact]
    public Task DashboardRetry_KeepsTheOriginalTime() => _scenarios.DashboardRetry_KeepsTheOriginalTime();

    [DockerFact]
    public Task Dashboard_ShowsTheOriginalTimeWhenItDiffers_AndCancelKeepsIt() => _scenarios.Dashboard_ShowsTheOriginalTimeWhenItDiffers_AndCancelKeepsIt();

    [DockerFact]
    public Task RowWithoutOriginalTime_SeesItsScheduledTime() => _scenarios.RowWithoutOriginalTime_SeesItsScheduledTime();

    [DockerFact]
    public Task ContextPassedAtEnqueue_IsNotStored() => _scenarios.ContextPassedAtEnqueue_IsNotStored();
}
