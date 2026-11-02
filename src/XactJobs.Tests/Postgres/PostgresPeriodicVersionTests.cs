using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests.Postgres;

[Collection(PostgresCollection.Name)]
public sealed class PostgresPeriodicVersionTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string Daily = "0 0 0 * * *";
    private const string EverySecond = "* * * * * *";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private readonly List<IHost> _hosts = [];
    private readonly VersionJobState _state = new();
    private readonly LogCapture _logs = new();

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
    public async Task SuccessfulRuns_ScheduleTheNextRun()
    {
        var host = CreateHost();

        await EnsurePeriodicAsync(host, EverySecond);

        await host.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.Runs >= 3), "three runs");

        Assert.False(_logs.HasErrors, _logs.ToString());
    }

    [PostgresFact]
    public async Task FailedRuns_AreRescheduledWithTheSameVersion()
    {
        var host = CreateHost(retryStrategy: new DefaultRetryStrategy(maxAttempts: 100, retrySeconds: [0]));

        var version = await WithDbAsync(host, async db =>
        {
            var periodicJob = db.JobEnsurePeriodic<VersionJob>(x => x.Fail(), "failing", Daily);

            await db.SaveChangesAsync();

            await db.Jobs.Where(j => j.PeriodicJobId == "failing")
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.ScheduledAt, DateTime.UtcNow.AddMinutes(-1)));

            return periodicJob.Version;
        });

        await host.StartAsync();

        await WaitForAsync(() => WithDbAsync(host, db => db.JobHistory.AnyAsync(h => h.PeriodicJobId == "failing" && h.ErrorCount >= 3)), "three failed attempts");

        await WaitForAsync(() => WithDbAsync(host, db => db.Jobs.AnyAsync(j => j.PeriodicJobId == "failing" && j.PeriodicJobVersion == version)), "the pending retry");

        Assert.False(_logs.Contains("Failed recording"), _logs.ToString());
    }

    [PostgresFact]
    public async Task RunsWithRetriesExhausted_ScheduleTheNextRun()
    {
        var host = CreateHost(retryStrategy: new DefaultRetryStrategy(maxAttempts: 1));

        await WithDbAsync(host, db =>
        {
            db.JobEnsurePeriodic<VersionJob>(x => x.Fail(), "cancelled", EverySecond);

            return db.SaveChangesAsync();
        });

        await host.StartAsync();

        await WaitForAsync(() => WithDbAsync(host, async db =>
            await db.JobHistory.CountAsync(h => h.PeriodicJobId == "cancelled" && h.Status == XactJobStatus.Cancelled) >= 2), "two cancelled runs");

        Assert.False(_logs.Contains("Failed recording"), _logs.ToString());
    }

    [PostgresFact]
    public async Task TwoNodesChangingADefinitionAtOnce_LeaveOneLiveRow()
    {
        var host = CreateHost();

        await EnsurePeriodicAsync(host, Daily);

        using var scopeA = host.Services.CreateScope();
        using var scopeB = host.Services.CreateScope();

        var nodeA = scopeA.ServiceProvider.GetRequiredService<TestDbContext>();
        var nodeB = scopeB.ServiceProvider.GetRequiredService<TestDbContext>();

        // both read before either saves
        nodeA.JobEnsurePeriodic<VersionJob>(x => x.Run(), "shared", EverySecond);
        nodeB.JobEnsurePeriodic<VersionJob>(x => x.Run(), "shared", EverySecond);

        await nodeA.SaveChangesAsync();
        await nodeB.SaveChangesAsync();

        var current = await WithDbAsync(host, db => db.PeriodicJobs.AsNoTracking().Where(p => p.Id == "shared").Select(p => p.Version).SingleAsync());
        var rows = await WithDbAsync(host, db => db.Jobs.AsNoTracking().Where(j => j.PeriodicJobId == "shared").ToListAsync());

        Assert.Equal(3, rows.Count); // the original, and one from each node
        Assert.Single(rows, j => j.PeriodicJobVersion == current);
    }

    [PostgresFact]
    public async Task SeveralNodesStartingWithAChangedDefinition_ChangeItOnlyOnce()
    {
        var seed = CreateHost();

        var originalVersion = await WithDbAsync(seed, async db =>
        {
            var periodicJob = db.JobEnsurePeriodic<VersionJob>(x => x.Run(), "from-options", Daily);

            await db.SaveChangesAsync();

            return periodicJob.Version;
        });

        IHost[] nodes = Enumerable.Range(0, 3)
            .Select(_ => CreateHost(configure: options => options.WithPeriodicJob<VersionJob>(x => x.Run(), "from-options", "0 0 1 * * *")))
            .ToArray();

        await Task.WhenAll(nodes.Select(node => node.StartAsync()));

        await WaitForAsync(() => WithDbAsync(seed, db => db.PeriodicJobs.AnyAsync(p => p.Id == "from-options" && p.Version != originalVersion)), "the change");

        // a few more cycles on all the nodes
        await Task.Delay(TimeSpan.FromSeconds(3));

        var current = await WithDbAsync(seed, db => db.PeriodicJobs.AsNoTracking().Where(p => p.Id == "from-options").Select(p => p.Version).SingleAsync());
        var rows = await WithDbAsync(seed, db => db.Jobs.AsNoTracking().Where(j => j.PeriodicJobId == "from-options").ToListAsync());

        Assert.Equal(2, rows.Count); // the original, for a worker to skip, and the current one
        Assert.Single(rows, j => j.PeriodicJobVersion == current);
        Assert.False(_logs.HasErrors, _logs.ToString());
    }

    [PostgresFact]
    public async Task DeleteAndAddAgain_LeavesOneLiveRow()
    {
        var host = CreateHost();

        await EnsurePeriodicAsync(host, Daily);

        await WithDbAsync(host, async db =>
        {
            await db.JobDeletePeriodicAsync("shared", CancellationToken.None);

            return await db.SaveChangesAsync();
        });

        await EnsurePeriodicAsync(host, Daily);

        var current = await WithDbAsync(host, db => db.PeriodicJobs.AsNoTracking().Where(p => p.Id == "shared").Select(p => p.Version).SingleAsync());
        var rows = await WithDbAsync(host, db => db.Jobs.AsNoTracking().Where(j => j.PeriodicJobId == "shared").ToListAsync());

        Assert.Equal(2, rows.Count); // the old one, for a worker to skip, and the new one
        Assert.Single(rows, j => j.PeriodicJobVersion == current);
    }

    private IHost CreateHost(IRetryStrategy? retryStrategy = null, Action<XactJobsOptionsBuilder<TestDbContext>>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);

        builder.Services.AddDbContext<TestDbContext>(options => options.UseNpgsql(_connectionString));

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options
                .WithWorkerCount(1)
                .WithPollingInterval(1)
                .WithPeriodicJobReconciliationInterval(1);

            if (retryStrategy != null)
            {
                options.WithRetryStrategy(retryStrategy);
            }

            configure?.Invoke(options);
        });

        builder.Services.AddSingleton(_state);
        builder.Services.AddScoped<VersionJob>();

        var host = builder.Build();

        _hosts.Add(host);

        return host;
    }

    private static Task<int> EnsurePeriodicAsync(IHost host, string cronExpression)
    {
        var id = cronExpression == EverySecond ? "running" : "shared";

        return WithDbAsync(host, db =>
        {
            db.JobEnsurePeriodic<VersionJob>(x => x.Run(), id, cronExpression);

            return db.SaveChangesAsync();
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

    public sealed class VersionJobState
    {
        private int _runs;

        public int Runs => Volatile.Read(ref _runs);

        public void OnRun() => Interlocked.Increment(ref _runs);
    }

    public sealed class VersionJob(VersionJobState state)
    {
        public void Run() => state.OnRun();

        public void Fail() => throw new InvalidOperationException("Failed on purpose");
    }

    private sealed class LogCapture : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        public bool HasErrors => _entries.Any(e => e.Level >= LogLevel.Error);

        public bool Contains(string text) => _entries.Any(e => e.Message.Contains(text));

        public override string ToString() => string.Join(Environment.NewLine, _entries.Select(e => $"{e.Level}: {e.Message}"));

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Information)
            {
                _entries.Enqueue((logLevel, $"{formatter(state, exception)} {exception}"));
            }
        }
    }
}
