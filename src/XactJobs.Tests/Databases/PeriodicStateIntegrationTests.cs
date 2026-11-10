using XactJobs.Tests.Postgres;

namespace XactJobs.Tests.Databases;

// The class names start with the database, so a filter like "XactJobs.Tests.Databases.Oracle" runs all the suites of one database.

[Collection(OfficialMySqlCollection.Name)]
public sealed class OfficialMySqlPeriodicStateTests(OfficialMySqlFixture fixture) : PeriodicStateIntegrationTests(fixture);

#if !NET10_0_OR_GREATER // Pomelo doesn't support EF Core 10 yet
[Collection(PomeloMySqlCollection.Name)]
public sealed class PomeloMySqlPeriodicStateTests(PomeloMySqlFixture fixture) : PeriodicStateIntegrationTests(fixture);
#endif

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerPeriodicStateTests(SqlServerFixture fixture) : PeriodicStateIntegrationTests(fixture);

[Collection(OracleCollection.Name)]
public sealed class OraclePeriodicStateTests(OracleFixture fixture) : PeriodicStateIntegrationTests(fixture);

[Collection(Postgres16Collection.Name)]
public sealed class Postgres16PeriodicStateTests(Postgres16Fixture fixture) : PeriodicStateIntegrationTests(fixture);

[Collection(Postgres17Collection.Name)]
public sealed class Postgres17PeriodicStateTests(Postgres17Fixture fixture) : PeriodicStateIntegrationTests(fixture);

[Collection(Postgres18Collection.Name)]
public sealed class Postgres18PeriodicStateTests(Postgres18Fixture fixture) : PeriodicStateIntegrationTests(fixture);

/// <summary>
/// <see cref="PeriodicStateScenarios"/> on every database, with the schema from the SQL scripts.
/// </summary>
public abstract class PeriodicStateIntegrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly PeriodicStateScenarios _scenarios = new(options => fixture.Configure(options, retryOnFailure: false));

    public async Task InitializeAsync()
    {
        if (DatabaseFixture.IsDockerAvailable)
        {
            await fixture.PrepareForTestAsync();
        }
    }

    public async Task DisposeAsync() => await _scenarios.DisposeAsync();

    [DockerFact]
    public Task EachRun_GetsTheStateThePreviousRunSet() => _scenarios.EachRun_GetsTheStateThePreviousRunSet();

    [DockerFact]
    public Task FailedAttempt_LeavesTheState_AndItsRetryGetsTheSame() => _scenarios.FailedAttempt_LeavesTheState_AndItsRetryGetsTheSame();

    [DockerFact]
    public Task RunWithoutSet_KeepsTheState_AndNullClearsIt() => _scenarios.RunWithoutSet_KeepsTheState_AndNullClearsIt();

    [DockerFact]
    public Task LongestState_RoundTrips_AndALongerOneFailsTheAttempt() => _scenarios.LongestState_RoundTrips_AndALongerOneFailsTheAttempt();

    [DockerFact]
    public Task OneOffJob_HasNoState_AndCantSetOne() => _scenarios.OneOffJob_HasNoState_AndCantSetOne();

    [DockerFact]
    public Task DefinitionChange_KeepsTheState() => _scenarios.DefinitionChange_KeepsTheState();

    [DockerFact]
    public Task Dashboard_ShowsTheState_AndDoesntRetryARunWithOne() => _scenarios.Dashboard_ShowsTheState_AndDoesntRetryARunWithOne();
}
