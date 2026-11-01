using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

public class SqlitePeriodicReconciliationTests : IAsyncLifetime
{
    private const string EverySecond = "* * * * * *";
    private const string Daily = "0 0 0 * * *";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private readonly ReconciliationJobState _state = new();
    private readonly QueueLoggerProvider _logs = new();

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private IHost? _host;

    public SqlitePeriodicReconciliationTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
    }

    public Task InitializeAsync() => _memoryConnection.OpenAsync();

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await _memoryConnection.DisposeAsync();
    }

    [Fact]
    public async Task RowInsertedDirectlyWithSql_GetsScheduled_AndKeepsRunning()
    {
        await CreateHostAsync();

        await InsertPeriodicRowWithSqlAsync("manual", EverySecond);

        await _host!.StartAsync();

        // more than one run: the chain continues from the first scheduled run
        await WaitForAsync(() => Task.FromResult(_state.Count >= 2), "the job to run twice");
    }

    [Fact]
    public async Task DeletedPendingRow_GetsRescheduled_ExactlyOnce()
    {
        await CreateHostAsync();

        await WithDbAsync(async db =>
        {
            db.JobEnsurePeriodic<ReconciliationJob>(x => x.Run(), "chain", Daily);

            return await db.SaveChangesAsync();
        });

        await WithDbAsync(db => db.Jobs.ExecuteDeleteAsync());

        await _host!.StartAsync();

        await WaitForAsync(() => WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == "chain")), "the run to be scheduled");

        await WaitForCyclesAsync(2);

        var job = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == "chain"));

        var periodicJob = await WithDbAsync(db => db.PeriodicJobs.AsNoTracking().SingleAsync(p => p.Id == "chain"));

        Assert.Equal(periodicJob.Version, job.PeriodicJobVersion);
        Assert.True(job.ScheduledAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task PendingRowOfOlderVersionOnly_SchedulesTheCurrentVersion()
    {
        await CreateHostAsync();

        await InsertPeriodicRowWithSqlAsync("upgraded", Daily, version: 2);
        await InsertPendingRowAsync("upgraded", Daily, version: 1);

        await _host!.StartAsync();

        await WaitForAsync(() => WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == "upgraded" && j.PeriodicJobVersion == 2)), "the current version");

        // the old one is still there, for a worker to skip
        Assert.Equal(1, await WithDbAsync(db => db.Jobs.CountAsync(j => j.PeriodicJobId == "upgraded" && j.PeriodicJobVersion == 1)));
    }

    [Fact]
    public async Task PendingRowOfCurrentVersion_IsLeftAlone()
    {
        await CreateHostAsync();

        await InsertPeriodicRowWithSqlAsync("healthy", Daily);
        await InsertPendingRowAsync("healthy", Daily, version: 1);

        await _host!.StartAsync();

        await WaitForCyclesAsync(2);

        Assert.Equal(1, await WithDbAsync(db => db.Jobs.CountAsync(j => j.PeriodicJobId == "healthy")));
    }

    [Fact]
    public async Task PendingRowFromBeforeVersioning_CountsAsVersion1()
    {
        await CreateHostAsync();

        await InsertPeriodicRowWithSqlAsync("legacy", Daily);
        await InsertPendingRowAsync("legacy", Daily, version: null);

        await _host!.StartAsync();

        await WaitForCyclesAsync(2);

        Assert.Equal(1, await WithDbAsync(db => db.Jobs.CountAsync(j => j.PeriodicJobId == "legacy")));
    }

    [Fact]
    public async Task InactivePeriodicJob_IsNotScheduled()
    {
        await CreateHostAsync();

        await InsertPeriodicRowWithSqlAsync("inactive", Daily, isActive: false);

        await _host!.StartAsync();

        await WaitForCyclesAsync(2);

        Assert.False(await WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == "inactive")));
    }

    [Fact]
    public async Task InvalidCronExpression_DoesNotStopTheOtherPeriodicJobs()
    {
        await CreateHostAsync();

        await InsertPeriodicRowWithSqlAsync("broken", "not a cron expression");
        await InsertPeriodicRowWithSqlAsync("fine", Daily);

        await _host!.StartAsync();

        await WaitForAsync(() => WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == "fine")), "the valid job to be scheduled");

        Assert.False(await WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == "broken")));

        await WaitForAsync(() => Task.FromResult(_logs.Contains(LogLevel.Error, "broken")), "the error to be logged");
    }

    private async Task CreateHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString));

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options
                .WithWorkerCount(1)
                .WithPollingInterval(1)
                .WithPeriodicJobReconciliationInterval(1);
        });

        builder.Services.AddSingleton(_state);
        builder.Services.AddScoped<ReconciliationJob>();

        _host = builder.Build();

        await WithDbAsync(db => db.Database.EnsureCreatedAsync());
    }

    private Task<int> InsertPeriodicRowWithSqlAsync(string id, string cronExpression, int version = 1, bool isActive = true)
    {
        return WithDbAsync(db =>
        {
            var template = CreateTemplate(db);

            return db.Database.ExecuteSqlRawAsync(@"
INSERT INTO xact_jobs_job_periodic (id, created_at, updated_at, cron_expression, type_name, method_name, method_args, queue, is_active, version)
VALUES ({0}, {1}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8})",
                id, DateTime.UtcNow, cronExpression, template.TypeName, template.MethodName, template.MethodArgs, QueueNames.Default, isActive, version);
        });
    }

    private Task<int> InsertPendingRowAsync(string periodicJobId, string cronExpression, int? version)
    {
        return WithDbAsync(db =>
        {
            var template = CreateTemplate(db);

            db.Jobs.Add(new XactJob(0, DateTime.UtcNow.AddHours(1), template.TypeName, template.MethodName, template.MethodArgs, QueueNames.Default,
                                    periodicJobId, cronExpression, version));

            return db.SaveChangesAsync();
        });
    }

    private static XactJob CreateTemplate(TestDbContext db)
    {
        var template = db.JobEnqueue<ReconciliationJob>(x => x.Run());

        db.ChangeTracker.Clear();

        return template;
    }

    /// <summary>
    /// The interval is 1 second, so this covers the given number of reconciliation cycles.
    /// </summary>
    private static Task WaitForCyclesAsync(int cycles) => Task.Delay(TimeSpan.FromSeconds(cycles + 0.5));

    private async Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        using var scope = _host!.Services.CreateScope();

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

    public sealed class ReconciliationJobState
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public void OnRun() => Interlocked.Increment(ref _count);
    }

    public sealed class ReconciliationJob(ReconciliationJobState state)
    {
        public void Run() => state.OnRun();
    }

    private sealed class QueueLoggerProvider : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        public bool Contains(LogLevel level, string text) => _entries.Any(e => e.Level == level && e.Message.Contains(text));

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
