using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

public class SqliteConcurrentPollingTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private readonly PollingJobState _state = new();
    private readonly QueueLoggerProvider _logs = new();

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private IHost? _host;

    public SqliteConcurrentPollingTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
    }

    public Task InitializeAsync() => _memoryConnection.OpenAsync();

    public async Task DisposeAsync()
    {
        _state.ReleaseAll();

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await _memoryConnection.DisposeAsync();
    }

    [Fact]
    public async Task SlowJob_DoesNotStopShortJobsOnTheSameWorker()
    {
        const int shortJobs = 20;

        await CreateHostAsync(capacity: 2);

        await WithDbAsync(db =>
        {
            db.JobEnqueue<PollingJob>(x => x.BlockUntilReleasedAsync());

            return db.SaveChangesAsync();
        });

        await _host!.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 1), "the slow job to start");

        // added now, so that they can only run if the worker keeps polling while the slow job is running
        await WithDbAsync(db =>
        {
            for (var i = 0; i < shortJobs; i++)
            {
                db.JobEnqueue<PollingJob>(x => x.Quick());
            }

            return db.SaveChangesAsync();
        });

        await WaitForAsync(() => Task.FromResult(_state.QuickCount == shortJobs), "the short jobs to finish");

        Assert.Equal(0, _state.ReleasedCount);
        Assert.Equal(shortJobs, await WithDbAsync(db => db.JobHistory.CountAsync()));

        _state.ReleaseAll();

        await WaitForAsync(() => WithDbAsync(async db => await db.JobHistory.CountAsync() == shortJobs + 1), "the slow job to finish");
    }

    [Fact]
    public async Task SynchronouslyBlockingJob_DoesNotStopThePollingLoop()
    {
        const int shortJobs = 10;

        await CreateHostAsync(capacity: 2);

        await WithDbAsync(db =>
        {
            db.JobEnqueue<PollingJob>(x => x.BlockSynchronously());

            return db.SaveChangesAsync();
        });

        await _host!.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 1), "the blocking job to start");

        await WithDbAsync(db =>
        {
            for (var i = 0; i < shortJobs; i++)
            {
                db.JobEnqueue<PollingJob>(x => x.Quick());
            }

            return db.SaveChangesAsync();
        });

        await WaitForAsync(() => Task.FromResult(_state.QuickCount == shortJobs), "the short jobs to finish");
    }

    [Fact]
    public async Task Backlog_DoesNotWaitForThePollingInterval()
    {
        const int jobs = 40;

        // waiting for the interval it would take 100 seconds: 20 polls, 5 seconds apart
        await CreateHostAsync(capacity: 2, pollingInterval: 5);

        await WithDbAsync(db =>
        {
            for (var i = 0; i < jobs; i++)
            {
                db.JobEnqueue<PollingJob>(x => x.Quick());
            }

            return db.SaveChangesAsync();
        });

        await _host!.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.QuickCount == jobs), "the backlog to finish");
    }

    [Fact]
    public async Task NeverClaimsMoreThanCapacity_AndClaimsAgainWhenASlotFrees()
    {
        await CreateHostAsync(capacity: 2);

        await WithDbAsync(db =>
        {
            for (var i = 0; i < 5; i++)
            {
                db.JobEnqueue<PollingJob>(x => x.BlockUntilReleasedAsync());
            }

            return db.SaveChangesAsync();
        });

        await _host!.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 2), "two jobs to start");

        // more than one polling interval
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Equal(2, _state.BlockedCount);
        Assert.Equal(2, await WithDbAsync(db => db.Jobs.CountAsync(j => j.PeriodicJobId == null && j.Leaser != null)));

        _state.ReleaseOne();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 3), "the next job to start");

        Assert.Equal(2, await WithDbAsync(db => db.Jobs.CountAsync(j => j.PeriodicJobId == null && j.Leaser != null)));
    }

    [Fact]
    public async Task QuickPoll_WakesAnIdleWorker()
    {
        await CreateHostAsync(capacity: 2, pollingInterval: 300);

        await _host!.StartAsync();

        using (var scope = _host.Services.CreateScope())
        {
            var quickPoll = scope.ServiceProvider.GetRequiredService<QuickPoll<TestDbContext>>();

            // already due, so that it doesn't depend on the clocks of the app and the database
            quickPoll.DbContext.JobScheduleAt<PollingJob>(x => x.Quick(), DateTime.UtcNow.AddMinutes(-1));

            await quickPoll.DbContext.SaveChangesAsync();

            quickPoll.Notify(QueueNames.Default);
        }

        await WaitForAsync(() => Task.FromResult(_state.QuickCount == 1), "the job to run");
    }

    [Fact]
    public async Task FailingJob_DoesNotStopTheWorker()
    {
        await CreateHostAsync(capacity: 1);

        await WithDbAsync(db =>
        {
            db.JobScheduleAt<PollingJob>(x => x.Fail(), DateTime.UtcNow.AddHours(-1));
            db.JobEnqueue<PollingJob>(x => x.Quick());

            return db.SaveChangesAsync();
        });

        await _host!.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.QuickCount == 1), "the next job to run");

        var failed = await WithDbAsync(db => db.JobHistory.AsNoTracking().SingleAsync(h => h.MethodName == nameof(PollingJob.Fail)));

        Assert.Equal(XactJobStatus.Failed, failed.Status);
    }

    [Fact]
    public async Task FailedPoll_IsLogged_AndPollingResumes()
    {
        await CreateHostAsync(capacity: 2);

        await WithDbAsync(db => db.Database.ExecuteSqlRawAsync("ALTER TABLE xact_jobs_job RENAME TO xact_jobs_job_away"));

        await _host!.StartAsync();

        await WaitForAsync(() => Task.FromResult(_logs.Contains(LogLevel.Error, "Processing jobs failed")), "the failed poll");

        await WithDbAsync(db => db.Database.ExecuteSqlRawAsync("ALTER TABLE xact_jobs_job_away RENAME TO xact_jobs_job"));

        await WithDbAsync(db =>
        {
            db.JobEnqueue<PollingJob>(x => x.Quick());

            return db.SaveChangesAsync();
        });

        await WaitForAsync(() => Task.FromResult(_state.QuickCount == 1), "the job to run");
    }

    [Fact]
    public async Task Stop_LetsRunningJobsFinishWithinTheTimeout()
    {
        await CreateHostAsync(capacity: 2, shutdownTimeout: 30);

        var jobId = await WithDbAsync(async db =>
        {
            var job = db.JobEnqueue<PollingJob>(x => x.BlockUntilReleasedAsync());

            await db.SaveChangesAsync();

            return job.Id;
        });

        await _host!.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 1), "the job to start");

        var stopping = _host.StopAsync();

        // still running, so the lease stays
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.False(stopping.IsCompleted);

        _state.ReleaseAll();

        await stopping;

        var history = await WithDbAsync(db => db.JobHistory.AsNoTracking().SingleAsync(h => h.Id == jobId));

        Assert.Equal(XactJobStatus.Completed, history.Status);
        Assert.False(await WithDbAsync(db => db.Jobs.AnyAsync(j => j.Id == jobId)));
    }

    [Fact]
    public async Task Stop_AfterTheTimeout_CancelsJobsAndClearsOnlyTheirLeases()
    {
        await CreateHostAsync(capacity: 2, shutdownTimeout: 1);

        var (cancelledId, otherId, otherLeaser) = await WithDbAsync(async db =>
        {
            var cancelled = db.JobEnqueue<PollingJob>(x => x.BlockUntilCancelledAsync(CancellationToken.None));

            var leaser = Guid.NewGuid();

            var other = new XactJob(0, DateTime.UtcNow.AddMinutes(-1), "Type", "Method", "[]", QueueNames.Default, leasedUntil: DateTime.UtcNow.AddHours(1));

            db.Jobs.Add(other);

            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlRawAsync("UPDATE xact_jobs_job SET leaser = {0} WHERE id = {1}", leaser.ToString(), other.Id);

            return (cancelled.Id, other.Id, leaser);
        });

        var finishedId = await WithDbAsync(async db =>
        {
            var finished = db.JobEnqueue<PollingJob>(x => x.Quick());

            await db.SaveChangesAsync();

            return finished.Id;
        });

        await _host!.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 1), "the job to start");
        await WaitForAsync(() => Task.FromResult(_state.QuickCount == 1), "the short job to finish");
        await WaitForAsync(() => WithDbAsync(db => db.JobHistory.AnyAsync(h => h.Id == finishedId)), "the short job to be recorded");

        await _host.StopAsync();

        var cancelled = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == cancelledId));

        Assert.Null(cancelled.Leaser);
        Assert.Null(cancelled.LeasedUntil);
        Assert.Equal(0, cancelled.ErrorCount);
        Assert.Equal(1, await WithDbAsync(db => db.JobHistory.CountAsync()));

        var other = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == otherId));

        Assert.Equal(otherLeaser, other.Leaser);
    }

    [Fact]
    public async Task Stop_WithAJobIgnoringTheCancellation_ClearsItsLeaseAndWarns()
    {
        await CreateHostAsync(capacity: 2, shutdownTimeout: 0);

        var jobId = await WithDbAsync(async db =>
        {
            var job = db.JobEnqueue<PollingJob>(x => x.BlockUntilReleasedAsync());

            await db.SaveChangesAsync();

            return job.Id;
        });

        await _host!.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 1), "the job to start");

        await _host.StopAsync();

        Assert.True(_logs.Contains(LogLevel.Warning, "did not stop after being cancelled"));
        Assert.Null((await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId))).Leaser);
    }

    private async Task CreateHostAsync(int capacity, int pollingInterval = 1, int shutdownTimeout = 0)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString));

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options
                .WithWorkerCount(1)
                .WithPollingInterval(pollingInterval)
                .WithShutdownTimeout(shutdownTimeout);

            options.Options.MaxDegreeOfParallelism = capacity;
        });

        builder.Services.AddSingleton(_state);
        builder.Services.AddScoped<PollingJob>();

        _host = builder.Build();

        await WithDbAsync(db => db.Database.EnsureCreatedAsync());
    }

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

            await Task.Delay(20);
        }
    }

    public sealed class PollingJobState
    {
        private readonly SemaphoreSlim _release = new(0);
        private readonly ManualResetEventSlim _releaseSynchronous = new();
        private int _quickCount;
        private int _blockedCount;
        private int _releasedCount;
        private volatile bool _releaseAll;

        public int QuickCount => Volatile.Read(ref _quickCount);
        public int BlockedCount => Volatile.Read(ref _blockedCount);
        public int ReleasedCount => Volatile.Read(ref _releasedCount);

        public void OnQuick() => Interlocked.Increment(ref _quickCount);
        public void OnBlocked() => Interlocked.Increment(ref _blockedCount);

        public void ReleaseOne() => _release.Release();

        public void ReleaseAll()
        {
            _releaseAll = true;
            _release.Release(100);
            _releaseSynchronous.Set();
        }

        public void WaitSynchronously() => _releaseSynchronous.Wait(WaitTimeout);

        public async Task WaitForReleaseAsync()
        {
            if (!_releaseAll)
            {
                await _release.WaitAsync(WaitTimeout);
            }

            Interlocked.Increment(ref _releasedCount);
        }
    }

    public sealed class PollingJob(PollingJobState state)
    {
        public void Quick() => state.OnQuick();

        public void Fail() => throw new InvalidOperationException("Failed on purpose");

        public void BlockSynchronously()
        {
            state.OnBlocked();
            state.WaitSynchronously();
        }

        public async Task BlockUntilReleasedAsync()
        {
            state.OnBlocked();

            await state.WaitForReleaseAsync();
        }

        public async Task BlockUntilCancelledAsync(CancellationToken cancellationToken)
        {
            state.OnBlocked();

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
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
            if (logLevel >= LogLevel.Warning)
            {
                _entries.Enqueue((logLevel, formatter(state, exception)));
            }
        }
    }
}
