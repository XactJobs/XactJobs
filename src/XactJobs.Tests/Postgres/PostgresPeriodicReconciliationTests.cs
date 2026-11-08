using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests.Postgres;

[Collection(Postgres16Collection.Name)]
public sealed class Postgres16PeriodicReconciliationTests(Postgres16Fixture fixture) : PostgresPeriodicReconciliationTests(fixture);

[Collection(Postgres17Collection.Name)]
public sealed class Postgres17PeriodicReconciliationTests(Postgres17Fixture fixture) : PostgresPeriodicReconciliationTests(fixture);

[Collection(Postgres18Collection.Name)]
public sealed class Postgres18PeriodicReconciliationTests(Postgres18Fixture fixture) : PostgresPeriodicReconciliationTests(fixture);

public abstract class PostgresPeriodicReconciliationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Daily = "0 0 0 * * *";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private readonly List<IHost> _hosts = [];

    private string _connectionString = "";

    public async Task InitializeAsync()
    {
        if (PostgresFixture.IsDockerAvailable)
        {
            _connectionString = await fixture.CreateDatabaseAsync();
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [PostgresFact]
    public async Task RowInsertedDirectlyWithSql_GetsScheduled()
    {
        var host = CreateHost();

        await InsertPeriodicRowWithSqlAsync(host, "manual");

        await host.StartAsync();

        await WaitForAsync(() => WithDbAsync(host, db => db.Jobs.AnyAsync(j => j.PeriodicJobId == "manual" && j.PeriodicJobVersion == 1)), "the run to be scheduled");
    }

    [PostgresFact]
    public async Task DeletedPendingRow_GetsRescheduled()
    {
        var host = CreateHost();

        await WithDbAsync(host, async db =>
        {
            db.JobEnsurePeriodic<PostgresReconciliationJob>(x => x.Run(), "chain", Daily);

            return await db.SaveChangesAsync();
        });

        await WithDbAsync(host, db => db.Jobs.ExecuteDeleteAsync());

        await host.StartAsync();

        await WaitForAsync(() => WithDbAsync(host, db => db.Jobs.AnyAsync(j => j.PeriodicJobId == "chain")), "the run to be rescheduled");
    }

    [PostgresFact]
    public async Task SeveralNodes_ScheduleTheMissingRunOnlyOnce()
    {
        IHost[] nodes = [CreateHost(), CreateHost(), CreateHost()];

        await InsertPeriodicRowWithSqlAsync(nodes[0], "shared");

        await Task.WhenAll(nodes.Select(node => node.StartAsync()));

        await WaitForAsync(() => WithDbAsync(nodes[0], db => db.Jobs.AnyAsync(j => j.PeriodicJobId == "shared")), "the run to be scheduled");

        // a few more cycles on all the nodes
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Equal(1, await WithDbAsync(nodes[0], db => db.Jobs.CountAsync(j => j.PeriodicJobId == "shared")));
    }

    [PostgresFact]
    public async Task WithRetryingExecutionStrategy_PeriodicJobsFromOptionsAreCreated()
    {
        var host = CreateHost(retryOnFailure: true, configure: options =>
        {
            options.WithPeriodicJob<PostgresReconciliationJob>(x => x.Run(), "from-options", Daily);
        });

        await host.StartAsync();

        await WaitForAsync(() => WithDbAsync(host, db => db.Jobs.AnyAsync(j => j.PeriodicJobId == "from-options")), "the periodic job to be created");
    }

    private IHost CreateHost(bool retryOnFailure = false, Action<XactJobsOptionsBuilder<TestDbContext>>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<TestDbContext>(options => options.UseNpgsql(_connectionString, npgsql =>
        {
            if (retryOnFailure)
            {
                npgsql.EnableRetryOnFailure();
            }
        }));

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options
                .WithWorkerCount(1)
                .WithPollingInterval(1)
                .WithPeriodicJobReconciliationInterval(1);

            configure?.Invoke(options);
        });

        builder.Services.AddScoped<PostgresReconciliationJob>();

        var host = builder.Build();

        _hosts.Add(host);

        return host;
    }

    private static Task<int> InsertPeriodicRowWithSqlAsync(IHost host, string id)
    {
        return WithDbAsync(host, db =>
        {
            var template = db.JobEnqueue<PostgresReconciliationJob>(x => x.Run());

            db.ChangeTracker.Clear();

            return db.Database.ExecuteSqlRawAsync(@"
INSERT INTO xact_jobs.job_periodic (id, created_at, updated_at, cron_expression, type_name, method_name, method_args, queue, is_active)
VALUES ({0}, now(), now(), {1}, {2}, {3}, {4}, {5}, true)",
                id, Daily, template.TypeName, template.MethodName, template.MethodArgs, QueueNames.Default);
        });
    }

    private static async Task<T> WithDbAsync<T>(IHost host, Func<TestDbContext, Task<T>> action)
    {
        using var scope = host.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, string description)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {description}");

            await Task.Delay(50);
        }
    }

    public sealed class PostgresReconciliationJob
    {
        public void Run()
        {
        }
    }
}
