using System.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;

namespace XactJobs.Tests.Postgres;

/// <summary>
/// One Postgres container for all the Postgres tests. Each test gets its own database,
/// created with the scripts in the sql folder, so the migration scripts are tested too.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private static readonly Lazy<bool> DockerAvailable = new(CheckDockerAvailable);

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16").Build();

    public static bool IsDockerAvailable => DockerAvailable.Value;

    public async Task InitializeAsync()
    {
        if (IsDockerAvailable)
        {
            await _container.StartAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public async Task<string> CreateDatabaseAsync()
    {
        var databaseName = $"test_{Guid.NewGuid():N}";

        await using (var connection = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await connection.OpenAsync();

            await using var command = new NpgsqlCommand($"CREATE DATABASE {databaseName}", connection);

            await command.ExecuteNonQueryAsync();
        }

        var connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName
        }.ConnectionString;

        await using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();

            foreach (var script in GetMigrationScripts())
            {
                await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(script), connection);

                await command.ExecuteNonQueryAsync();
            }
        }

        return connectionString;
    }

    private static IEnumerable<string> GetMigrationScripts()
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
            .Select(d => Path.Combine(d.FullName, $"xact-jobs-postgresql-{d.Name}.sql"))
            .Where(File.Exists);
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
        if (!PostgresFixture.IsDockerAvailable)
        {
            Skip = "Docker is not running";
        }
    }
}
