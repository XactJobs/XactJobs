using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Testcontainers.Oracle;
using XactJobs.Internal.SqlDialects;
using MySql.Data.MySqlClient;
using Testcontainers.MySql;

namespace XactJobs.Tests.Databases;

/// <summary>
/// One container per database, shared by the tests of its collection, which run one at a time.
/// The schema comes from the scripts in the sql folder, so the migration scripts are tested too.
/// </summary>
public abstract class DatabaseFixture : IAsyncLifetime
{
    private static readonly Lazy<bool> DockerAvailable = new(CheckDockerAvailable);

    public static bool IsDockerAvailable => DockerAvailable.Value;

    /// <summary>
    /// The concrete dialect: the shared interface is internal to the library.
    /// </summary>
    public abstract dynamic Dialect { get; }

    public abstract bool SupportsRetryOnFailure { get; }

    /// <summary>
    /// Returns the number of indexes on the leaser column of the job table.
    /// </summary>
    public abstract string CountLeaserIndexesSql { get; }

    public abstract void Configure(DbContextOptionsBuilder options, bool retryOnFailure);

    protected abstract Task StartContainerAsync();

    protected abstract Task StopContainerAsync();

    protected abstract Task CreateSchemaAsync();

    public async Task InitializeAsync()
    {
        if (IsDockerAvailable)
        {
            await StartContainerAsync();
            await CreateSchemaAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await StopContainerAsync();
    }

    /// <summary>
    /// Gives a test an empty database.
    /// </summary>
    public virtual async Task PrepareForTestAsync()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>();

        Configure(options, retryOnFailure: false);

        await using var db = new TestDbContext(options.Options);

        await db.Jobs.ExecuteDeleteAsync();
        await db.JobHistory.ExecuteDeleteAsync();
        await db.PeriodicJobs.ExecuteDeleteAsync();
    }

    protected static IEnumerable<string> GetMigrationScripts(string databaseName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "sql", "v0.1.0")))
        {
            directory = directory.Parent;
        }

        if (directory == null)
        {
            throw new DirectoryNotFoundException($"Could not find the sql folder above {AppContext.BaseDirectory}");
        }

        return new DirectoryInfo(Path.Combine(directory.FullName, "sql"))
            .GetDirectories("v*")
            .OrderBy(d => Version.Parse(d.Name[1..]))
            .Select(d => Path.Combine(d.FullName, $"xact-jobs-{databaseName}-{d.Name}.sql"))
            .Where(File.Exists);
    }

    protected static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        await command.ExecuteNonQueryAsync();
    }

    private static bool CheckDockerAvailable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            });

            if (process == null) return false;

            // the output is not read: discard it so the process can't block on a full pipe
            process.OutputDataReceived += (_, _) => { };
            process.ErrorDataReceived += (_, _) => { };
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (process.WaitForExit(TimeSpan.FromSeconds(15)))
            {
                return process.ExitCode == 0;
            }

            process.Kill();

            return false;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false; // docker is not installed
        }
    }
}

/// <summary>
/// A test that needs a database container. It's skipped if Docker isn't running.
/// </summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DatabaseFixture.IsDockerAvailable)
        {
            Skip = "Docker is not running";
        }
    }
}

public abstract class MySqlFixtureBase : DatabaseFixture
{
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.4.13").Build();

    protected string ConnectionString => _container.GetConnectionString();

    public override dynamic Dialect { get; } = new MySqlDialect();

    public override bool SupportsRetryOnFailure => true;

    public override string CountLeaserIndexesSql => "SELECT COUNT(*) FROM information_schema.statistics WHERE table_schema = DATABASE() AND table_name = 'xact_jobs_job' AND column_name = 'leaser'";

    protected override Task StartContainerAsync() => _container.StartAsync();

    protected override Task StopContainerAsync() => _container.DisposeAsync().AsTask();

    protected override async Task CreateSchemaAsync()
    {
        await using var connection = new MySqlConnection(ConnectionString);

        await connection.OpenAsync();

        foreach (var script in GetMigrationScripts("mysql"))
        {
            await ExecuteAsync(connection, await File.ReadAllTextAsync(script));
        }
    }
}

/// <summary>
/// The official provider, MySql.EntityFrameworkCore.
/// </summary>
public sealed class OfficialMySqlFixture : MySqlFixtureBase
{
    public override void Configure(DbContextOptionsBuilder options, bool retryOnFailure)
    {
        options.UseMySQL(ConnectionString, mySql =>
        {
            if (retryOnFailure)
            {
                mySql.EnableRetryOnFailure();
            }
        });
    }
}

#if !NET10_0_OR_GREATER // Pomelo doesn't support EF Core 10 yet
public sealed class PomeloMySqlFixture : MySqlFixtureBase
{
    public override void Configure(DbContextOptionsBuilder options, bool retryOnFailure)
    {
        options.UseMySql(ConnectionString, new MySqlServerVersion(new Version(8, 0, 36)), mySql =>
        {
            if (retryOnFailure)
            {
                mySql.EnableRetryOnFailure();
            }
        });
    }
}
#endif

public sealed class SqlServerFixture : DatabaseFixture
{
    private const string DatabaseName = "xact_jobs_tests";

    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU27-GDR1-ubuntu-22.04").Build();

    private string ConnectionString => new SqlConnectionStringBuilder(_container.GetConnectionString())
    {
        InitialCatalog = DatabaseName
    }.ConnectionString;

    public override dynamic Dialect { get; } = new SqlServerDialect();

    public override bool SupportsRetryOnFailure => true;

    public override string CountLeaserIndexesSql => @"
SELECT COUNT(*)
FROM sys.index_columns ic
JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE ic.object_id = OBJECT_ID('XactJobs.Job') AND c.name = 'Leaser'";

    public override void Configure(DbContextOptionsBuilder options, bool retryOnFailure)
    {
        options.UseSqlServer(ConnectionString, sqlServer =>
        {
            if (retryOnFailure)
            {
                sqlServer.EnableRetryOnFailure();
            }
        });
    }

    protected override Task StartContainerAsync() => _container.StartAsync();

    protected override Task StopContainerAsync() => _container.DisposeAsync().AsTask();

    protected override async Task CreateSchemaAsync()
    {
        await using (var master = new SqlConnection(_container.GetConnectionString()))
        {
            await master.OpenAsync();

            await ExecuteAsync(master, $"CREATE DATABASE {DatabaseName}");
        }

        await using var connection = new SqlConnection(ConnectionString);

        await connection.OpenAsync();

        foreach (var script in GetMigrationScripts("sqlserver"))
        {
            await ExecuteAsync(connection, await File.ReadAllTextAsync(script));
        }
    }
}

public sealed class OracleFixture : DatabaseFixture
{
    private readonly OracleContainer _container = new OracleBuilder("gvenzl/oracle-free:23.26.3-slim")
        .WithDatabase("xact_jobs_tests") // not the default one, which the image would then try to create again
        .Build();

    public override dynamic Dialect { get; } = new OracleDialect();

    public override bool SupportsRetryOnFailure => false;

    public override string CountLeaserIndexesSql => "SELECT COUNT(*) FROM user_ind_columns WHERE table_name = 'XACT_JOBS_JOB' AND column_name = 'LEASER'";

    public override void Configure(DbContextOptionsBuilder options, bool retryOnFailure)
    {
        options.UseOracle(_container.GetConnectionString());
    }

    protected override Task StartContainerAsync() => _container.StartAsync();

    protected override Task StopContainerAsync() => _container.DisposeAsync().AsTask();

    protected override async Task CreateSchemaAsync()
    {
        await using var connection = new OracleConnection(_container.GetConnectionString());

        await connection.OpenAsync();

        foreach (var script in GetMigrationScripts("oracle"))
        {
            // Oracle runs one statement at a time
            var statements = (await File.ReadAllTextAsync(script))
                .Split(';')
                .Select(RemoveComments)
                .Where(statement => statement.Length > 0);

            foreach (var statement in statements)
            {
                await ExecuteAsync(connection, statement);
            }
        }
    }

    private static string RemoveComments(string statement)
    {
        var lines = statement
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !line.StartsWith("--"));

        return string.Join('\n', lines).Trim();
    }
}

[CollectionDefinition(Name)]
public sealed class OfficialMySqlCollection : ICollectionFixture<OfficialMySqlFixture>
{
    public const string Name = "OfficialMySql";
}

#if !NET10_0_OR_GREATER
[CollectionDefinition(Name)]
public sealed class PomeloMySqlCollection : ICollectionFixture<PomeloMySqlFixture>
{
    public const string Name = "PomeloMySql";
}
#endif

[CollectionDefinition(Name)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}

[CollectionDefinition(Name)]
public sealed class OracleCollection : ICollectionFixture<OracleFixture>
{
    public const string Name = "Oracle";
}
