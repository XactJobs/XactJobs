using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using XactJobs.Internal.SqlDialects;
using XactJobs.Tests.Databases;

namespace XactJobs.Tests.Postgres;

/// <summary>
/// One Postgres container, of one major version, for all the Postgres tests of that version. Each test gets its own database,
/// created with the scripts in the sql folder, so the migration scripts are tested too.
/// </summary>
public abstract class PostgresFixture(string image) : DatabaseFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(image).Build();

    private string _connectionString = "";

    public override dynamic Dialect { get; } = new PostgreSqlDialect();

    public override bool SupportsRetryOnFailure => true;

    public override string CountLeaserIndexesSql => "SELECT COUNT(*) FROM pg_indexes WHERE schemaname = 'xact_jobs' AND tablename = 'job' AND indexdef LIKE '%leaser%'";

    public override void Configure(DbContextOptionsBuilder options, bool retryOnFailure)
    {
        options.UseNpgsql(_connectionString, npgsql =>
        {
            if (retryOnFailure)
            {
                npgsql.EnableRetryOnFailure();
            }
        });
    }

    public override async Task PrepareForTestAsync()
    {
        // each test gets its own database, so its own pool: close the idle connections of the previous test's pool, which Npgsql
        // keeps open for 5 minutes, or the suite runs out of the server's 100 connections
        if (_connectionString != "")
        {
            await using var previous = new NpgsqlConnection(_connectionString);

            NpgsqlConnection.ClearPool(previous);
        }

        _connectionString = await CreateDatabaseAsync();
    }

    protected override Task StartContainerAsync() => _container.StartAsync();

    protected override Task StopContainerAsync() => _container.DisposeAsync().AsTask();

    protected override Task CreateSchemaAsync() => Task.CompletedTask; // done per test

    public async Task<string> CreateDatabaseAsync()
    {
        var databaseName = $"test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();

            await ExecuteAsync(connection, $"CREATE DATABASE {databaseName}");
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName
        }.ConnectionString;

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();

            foreach (var script in GetMigrationScripts("postgresql"))
            {
                await ExecuteAsync(connection, await File.ReadAllTextAsync(script));
            }
        }

        return connectionString;
    }
}

// The latest minor version of each major version that's supported, exact so that every machine tests the same ones
public sealed class Postgres16Fixture() : PostgresFixture("postgres:16.15");

public sealed class Postgres17Fixture() : PostgresFixture("postgres:17.11");

public sealed class Postgres18Fixture() : PostgresFixture("postgres:18.6");

[CollectionDefinition(Name)]
public sealed class Postgres16Collection : ICollectionFixture<Postgres16Fixture>
{
    public const string Name = "Postgres16";
}

[CollectionDefinition(Name)]
public sealed class Postgres17Collection : ICollectionFixture<Postgres17Fixture>
{
    public const string Name = "Postgres17";
}

[CollectionDefinition(Name)]
public sealed class Postgres18Collection : ICollectionFixture<Postgres18Fixture>
{
    public const string Name = "Postgres18";
}

/// <summary>
/// A test that needs the Postgres container. It's skipped if Docker isn't running.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (!DatabaseFixture.IsDockerAvailable)
        {
            Skip = "Docker is not running";
        }
    }
}
