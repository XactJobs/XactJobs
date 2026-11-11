using XactJobs.Tests.Postgres;

namespace XactJobs.Tests.Databases;

// The class names start with the database, so a filter like "XactJobs.Tests.Databases.Oracle" runs all the suites of one database.

[Collection(OfficialMySqlCollection.Name)]
public sealed class OfficialMySqlPeriodicRunIndexTests(OfficialMySqlFixture fixture) : PeriodicRunIndexIntegrationTests(fixture);

#if !NET10_0_OR_GREATER // Pomelo doesn't support EF Core 10 yet
[Collection(PomeloMySqlCollection.Name)]
public sealed class PomeloMySqlPeriodicRunIndexTests(PomeloMySqlFixture fixture) : PeriodicRunIndexIntegrationTests(fixture);
#endif

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerPeriodicRunIndexTests(SqlServerFixture fixture) : PeriodicRunIndexIntegrationTests(fixture);

[Collection(OracleCollection.Name)]
public sealed class OraclePeriodicRunIndexTests(OracleFixture fixture) : PeriodicRunIndexIntegrationTests(fixture);

[Collection(Postgres16Collection.Name)]
public sealed class Postgres16PeriodicRunIndexTests(Postgres16Fixture fixture) : PeriodicRunIndexIntegrationTests(fixture);

[Collection(Postgres17Collection.Name)]
public sealed class Postgres17PeriodicRunIndexTests(Postgres17Fixture fixture) : PeriodicRunIndexIntegrationTests(fixture);

[Collection(Postgres18Collection.Name)]
public sealed class Postgres18PeriodicRunIndexTests(Postgres18Fixture fixture) : PeriodicRunIndexIntegrationTests(fixture);

/// <summary>
/// <see cref="PeriodicRunIndexScenarios"/> on every database, with the schema from the SQL scripts.
/// </summary>
public abstract class PeriodicRunIndexIntegrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly PeriodicRunIndexScenarios _scenarios = new(options => fixture.Configure(options, retryOnFailure: false));

    public async Task InitializeAsync()
    {
        if (DatabaseFixture.IsDockerAvailable)
        {
            await fixture.PrepareForTestAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [DockerFact]
    public Task SecondRunOfAVersion_IsRefused() => _scenarios.SecondRunOfAVersion_IsRefused();

    [DockerFact]
    public Task Dashboard_TriggerAndResume_WhenARunIsAddedAtTheSameTime_AreConflicts() => _scenarios.Dashboard_TriggerAndResume_WhenARunIsAddedAtTheSameTime_AreConflicts();
}
