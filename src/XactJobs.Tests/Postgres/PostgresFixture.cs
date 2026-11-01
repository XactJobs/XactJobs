using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using XactJobs.Internal.SqlDialects;
using XactJobs.Tests.Databases;

namespace XactJobs.Tests.Postgres;

/// <summary>
/// One Postgres container for all the Postgres tests. Each test gets its own database,
/// created with the scripts in the sql folder, so the migration scripts are tested too.
/// </summary>
public sealed class PostgresFixture : DatabaseFixture
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

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

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
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
