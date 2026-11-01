using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

public class SqliteJobFailureRecordingTests : IAsyncLifetime
{
    private const int ParallelJobCount = 4;

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private readonly FailureGate _gate = new(ParallelJobCount);
    private readonly CapturingLoggerProvider _logs = new();

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private IHost? _host;

    public SqliteJobFailureRecordingTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
    }

    public Task InitializeAsync() => _memoryConnection.OpenAsync();

    public async Task DisposeAsync()
    {
        _gate.Release();

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await _memoryConnection.DisposeAsync();
    }

    [Fact]
    public async Task ParallelFailures_EachGetErrorCountHistoryAndReschedule()
    {
        var retryAt = DateTime.UtcNow.AddHours(1);

        await CreateHostAsync(new FixedRetryStrategy(retryAt));

        var jobIds = await EnqueueFailingJobsAsync(ParallelJobCount);

        await _host!.StartAsync();

        await _gate.AllArrived.WaitAsync(WaitTimeout);
        _gate.Release();

        await WaitForHistoryCountAsync(ParallelJobCount);

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var history = await db.JobHistory.ToListAsync();

        Assert.Equal(jobIds.Order(), history.Select(h => h.Id).Order());
        Assert.All(history, h =>
        {
            Assert.Equal(XactJobStatus.Failed, h.Status);
            Assert.Equal(1, h.ErrorCount);
            Assert.Equal(FailingJob.ErrorMessage, h.ErrorMessage);
        });

        var pending = await db.Jobs.Where(j => j.PeriodicJobId == null).ToListAsync();

        Assert.Equal(ParallelJobCount, pending.Count);
        Assert.Empty(pending.Select(j => j.Id).Intersect(jobIds));
        Assert.All(pending, j =>
        {
            Assert.Equal(1, j.ErrorCount);
            Assert.Equal(retryAt, j.ScheduledAt, TimeSpan.FromSeconds(1));
        });
    }

    [Fact]
    public async Task ParallelFailures_WithRetriesExhausted_AreCancelledAndNotRescheduled()
    {
        await CreateHostAsync(new FixedRetryStrategy(null));

        var jobIds = await EnqueueFailingJobsAsync(ParallelJobCount);

        await _host!.StartAsync();

        await _gate.AllArrived.WaitAsync(WaitTimeout);
        _gate.Release();

        await WaitForHistoryCountAsync(ParallelJobCount);

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var history = await db.JobHistory.ToListAsync();

        Assert.Equal(jobIds.Order(), history.Select(h => h.Id).Order());
        Assert.All(history, h =>
        {
            Assert.Equal(XactJobStatus.Cancelled, h.Status);
            Assert.Equal(1, h.ErrorCount);
        });

        Assert.Empty(await db.Jobs.Where(j => j.PeriodicJobId == null).ToListAsync());
    }

    [Fact]
    public async Task Failure_WhenJobRowIsAlreadyGone_LogsWarningAndOtherFailuresAreRecorded()
    {
        await CreateHostAsync(new FixedRetryStrategy(DateTime.UtcNow.AddHours(1)));

        var jobIds = await EnqueueFailingJobsAsync(ParallelJobCount);
        var goneJobId = jobIds[0];
        var survivingJobIds = jobIds.Skip(1).ToList();

        await _host!.StartAsync();

        await _gate.AllArrived.WaitAsync(WaitTimeout);

        // same effect as the lease expiring and another worker processing the job
        using (var deleteScope = _host.Services.CreateScope())
        {
            var deleteDb = deleteScope.ServiceProvider.GetRequiredService<TestDbContext>();

            await deleteDb.Jobs.Where(j => j.Id == goneJobId).ExecuteDeleteAsync();
        }

        _gate.Release();

        await WaitForHistoryCountAsync(survivingJobIds.Count);
        await WaitForAsync(() => _logs.HasWarningFor(goneJobId), "warning for the job whose row is gone");

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var history = await db.JobHistory.ToListAsync();

        Assert.Equal(survivingJobIds.Order(), history.Select(h => h.Id).Order());

        var pending = await db.Jobs.Where(j => j.PeriodicJobId == null).ToListAsync();

        Assert.Equal(survivingJobIds.Count, pending.Count);
        Assert.All(pending, j => Assert.Equal(1, j.ErrorCount));
    }

    private async Task CreateHostAsync(IRetryStrategy retryStrategy)
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
                .WithRetryStrategy(retryStrategy);

            options.Options.MaxDegreeOfParallelism = ParallelJobCount;
        });

        builder.Services.AddSingleton(_gate);
        builder.Services.AddScoped<FailingJob>();

        _host = builder.Build();

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        await db.Database.EnsureCreatedAsync();
    }

    /// <summary>
    /// Saved before the host starts, so that the worker claims all of the jobs in one batch.
    /// </summary>
    private async Task<List<long>> EnqueueFailingJobsAsync(int count)
    {
        using var scope = _host!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var jobs = Enumerable.Range(0, count)
            .Select(_ => db.JobEnqueue<FailingJob>(x => x.FailWhenReleasedAsync()))
            .ToList();

        await db.SaveChangesAsync();

        return jobs.Select(j => j.Id).ToList();
    }

    private Task WaitForHistoryCountAsync(int expected)
    {
        return WaitForAsync(() =>
        {
            using var scope = _host!.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

            return db.JobHistory.Count() >= expected;
        }, $"{expected} history rows");
    }

    private static async Task WaitForAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {description}");

            await Task.Delay(50);
        }
    }

    public sealed class FailureGate(int expectedArrivals)
    {
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;

        public Task AllArrived => _allArrived.Task;

        public void Release() => _released.TrySetResult();

        public async Task ArriveAndWaitForReleaseAsync()
        {
            if (Interlocked.Increment(ref _arrivals) == expectedArrivals)
            {
                _allArrived.TrySetResult();
            }

            await _released.Task.WaitAsync(WaitTimeout);
        }
    }

    public sealed class FailingJob(FailureGate gate)
    {
        public const string ErrorMessage = "Failed on purpose";

        public async Task FailWhenReleasedAsync()
        {
            await gate.ArriveAndWaitForReleaseAsync();

            throw new InvalidOperationException(ErrorMessage);
        }
    }

    private sealed class FixedRetryStrategy(DateTime? retryAt) : IRetryStrategy
    {
        public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount) => retryAt;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        public bool HasWarningFor(long jobId)
        {
            return _entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains($"({jobId})"));
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<(LogLevel, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                entries.Enqueue((logLevel, formatter(state, exception)));
            }
        }
    }
}
