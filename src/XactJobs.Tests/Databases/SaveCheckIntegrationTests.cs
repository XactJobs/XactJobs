using XactJobs.Tests.Postgres;

namespace XactJobs.Tests.Databases;

// The class names start with the database, so a filter like "XactJobs.Tests.Databases.Oracle" runs all the suites of one database.

[Collection(OfficialMySqlCollection.Name)]
public sealed class OfficialMySqlSaveCheckTests(OfficialMySqlFixture fixture) : SaveCheckIntegrationTests(fixture);

#if !NET10_0_OR_GREATER // Pomelo doesn't support EF Core 10 yet
[Collection(PomeloMySqlCollection.Name)]
public sealed class PomeloMySqlSaveCheckTests(PomeloMySqlFixture fixture) : SaveCheckIntegrationTests(fixture);
#endif

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerSaveCheckTests(SqlServerFixture fixture) : SaveCheckIntegrationTests(fixture);

[Collection(OracleCollection.Name)]
public sealed class OracleSaveCheckTests(OracleFixture fixture) : SaveCheckIntegrationTests(fixture);

[Collection(Postgres16Collection.Name)]
public sealed class Postgres16SaveCheckTests(Postgres16Fixture fixture) : SaveCheckIntegrationTests(fixture);

[Collection(Postgres17Collection.Name)]
public sealed class Postgres17SaveCheckTests(Postgres17Fixture fixture) : SaveCheckIntegrationTests(fixture);

[Collection(Postgres18Collection.Name)]
public sealed class Postgres18SaveCheckTests(Postgres18Fixture fixture) : SaveCheckIntegrationTests(fixture);

/// <summary>
/// <see cref="SaveCheckScenarios"/> on every database, with the schema from the SQL scripts: whether a save outside a transaction is seen
/// depends on each provider starting EF's own transaction after the SavingChanges event.
/// </summary>
public abstract class SaveCheckIntegrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly SaveCheckScenarios _scenarios = new(options => fixture.Configure(options, retryOnFailure: false));

    public async Task InitializeAsync()
    {
        if (DatabaseFixture.IsDockerAvailable)
        {
            await fixture.PrepareForTestAsync();
        }
    }

    public async Task DisposeAsync() => await _scenarios.DisposeAsync();

    [DockerFact]
    public Task SaveWithoutTransaction_Warns_OncePerJobMethod() => _scenarios.SaveWithoutTransaction_Warns_OncePerJobMethod();

    [DockerFact]
    public Task SaveInsideTheJobsOwnTransaction_DoesntWarn() => _scenarios.SaveInsideTheJobsOwnTransaction_DoesntWarn();

    [DockerFact]
    public Task JobThatDoesntSave_DoesntWarn() => _scenarios.JobThatDoesntSave_DoesntWarn();

    [DockerFact]
    public Task ThrowMode_JobsThatSaveTheRightWay_CompleteOnTheFirstAttempt() => _scenarios.ThrowMode_JobsThatSaveTheRightWay_CompleteOnTheFirstAttempt();

    [DockerFact]
    public Task ThrowMode_SaveWithoutTransaction_Fails_AndIsRetried() => _scenarios.ThrowMode_SaveWithoutTransaction_Fails_AndIsRetried();

    [DockerFact]
    public Task IgnoreMode_SaveWithoutTransaction_DoesntWarn() => _scenarios.IgnoreMode_SaveWithoutTransaction_DoesntWarn();
}
