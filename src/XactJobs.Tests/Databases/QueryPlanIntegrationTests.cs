using XactJobs.Tests.Postgres;

namespace XactJobs.Tests.Databases;

// The class names start with the database, so a filter like "XactJobs.Tests.Databases.Oracle" runs all the suites of one database.

[Collection(OfficialMySqlCollection.Name)]
public sealed class OfficialMySqlQueryPlanTests(OfficialMySqlFixture fixture) : QueryPlanIntegrationTests(fixture);

#if !NET10_0_OR_GREATER // Pomelo doesn't support EF Core 10 yet
[Collection(PomeloMySqlCollection.Name)]
public sealed class PomeloMySqlQueryPlanTests(PomeloMySqlFixture fixture) : QueryPlanIntegrationTests(fixture);
#endif

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerQueryPlanTests(SqlServerFixture fixture) : QueryPlanIntegrationTests(fixture);

[Collection(OracleCollection.Name)]
public sealed class OracleQueryPlanTests(OracleFixture fixture) : QueryPlanIntegrationTests(fixture);

[Collection(Postgres16Collection.Name)]
public sealed class Postgres16QueryPlanTests(Postgres16Fixture fixture) : QueryPlanIntegrationTests(fixture);

[Collection(Postgres17Collection.Name)]
public sealed class Postgres17QueryPlanTests(Postgres17Fixture fixture) : QueryPlanIntegrationTests(fixture);

[Collection(Postgres18Collection.Name)]
public sealed class Postgres18QueryPlanTests(Postgres18Fixture fixture) : QueryPlanIntegrationTests(fixture);

/// <summary>
/// <see cref="QueryPlanScenarios"/> on every database, with the schema from the SQL scripts.
/// </summary>
public abstract class QueryPlanIntegrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private readonly QueryPlanScenarios _scenarios = new(options => fixture.Configure(options, retryOnFailure: false));

    public async Task InitializeAsync()
    {
        if (DatabaseFixture.IsDockerAvailable)
        {
            await fixture.PrepareForTestAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [DockerFact]
    public Task DashboardJobLookups_UseThePrimaryKey() => _scenarios.DashboardJobLookups_UseThePrimaryKey();

    [DockerFact]
    public async Task PeriodicJobQueries_UseThePeriodicJobIndex()
    {
        // MySQL has no index of the periodic job column (no filtered indexes, and a full one would be written by every job)
        if (fixture.Dialect is XactJobs.Internal.SqlDialects.MySqlDialect) return;

        await _scenarios.PeriodicJobQueries_UseThePeriodicJobIndex();
    }

    [DockerFact]
    public Task BatchQueries_UseTheBatchIndexes() => _scenarios.BatchQueries_UseTheBatchIndexes();
}
