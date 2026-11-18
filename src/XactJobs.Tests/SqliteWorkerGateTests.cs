using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

/// <summary>
/// The worker gate: asked before every claim, it stops the claims of this node's workers, not the jobs they hold.
/// </summary>
public class SqliteWorkerGateTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private readonly Gate _gate = new();
    private readonly Probe _probe = new();
    private readonly LogCollector _logs = new();

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private IHost? _host;

    public SqliteWorkerGateTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
    }

    public Task InitializeAsync() => _memoryConnection.OpenAsync();

    public async Task DisposeAsync()
    {
        _probe.Release.TrySetResult();

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await _memoryConnection.DisposeAsync();
    }

    [Fact]
    public async Task ClosedGate_ClaimsNothing_UntilItOpens()
    {
        _gate.Open = false;

        await CreateHostAsync();

        var job = await EnqueueAsync(db => db.JobEnqueue<GateJobs>(x => x.Run()));

        await _host!.StartAsync();

        // asked at a few polls, and the job stays pending, without a lease
        await WaitForAsync(() => _gate.Calls.Count >= 3, "the gate's calls");

        var pending = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id));
        Assert.Null(pending.Leaser);
        Assert.All(_gate.Calls, q => Assert.Equal(QueueNames.Default, q));

        _gate.Open = true;

        var history = await WaitForHistoryAsync(job.Id);
        Assert.Equal(XactJobStatus.Completed, history.Status);

        // once closed (the first answer), once open again
        Assert.Equal(1, _logs.Count("The worker gate is closed"));
        Assert.Equal(1, _logs.Count("The worker gate is open"));
    }

    [Fact]
    public async Task GateThatThrows_CountsAsClosed_IsLogged_AndTheWorkerKeepsPolling()
    {
        _gate.Throw = true;

        await CreateHostAsync();

        var job = await EnqueueAsync(db => db.JobEnqueue<GateJobs>(x => x.Run()));

        await _host!.StartAsync();

        await WaitForAsync(() => _logs.Count("The worker gate failed") >= 2, "the gate's failures");

        Assert.Null((await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id))).Leaser);
        Assert.Equal(1, _logs.Count("The worker gate is closed"));

        _gate.Throw = false;

        var history = await WaitForHistoryAsync(job.Id);
        Assert.Equal(XactJobStatus.Completed, history.Status);
        Assert.Equal(1, _logs.Count("The worker gate is open"));
    }

    [Fact]
    public async Task ClosingTheGate_LetsTheHeldJobFinish_WithItsLeaseExtended()
    {
        await CreateHostAsync(leaseSeconds: 2);

        var running = await EnqueueAsync(db => db.JobEnqueue<GateJobs>(x => x.Block()));

        await _host!.StartAsync();

        await _probe.Started.Task.WaitAsync(WaitTimeout);

        _gate.Open = false;

        var callsWhenClosed = _gate.Calls.Count;
        var waiting = await EnqueueAsync(db => db.JobEnqueue<GateJobs>(x => x.Run()));

        // the next poll asks the gate, and claims nothing
        await WaitForAsync(() => _gate.Calls.Count > callsWhenClosed + 1, "the gate's calls");

        // past the first lease: the timer extended it
        await Task.Delay(TimeSpan.FromSeconds(2.5));

        var held = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == running.Id));
        Assert.NotNull(held.Leaser);
        Assert.True(held.LeasedUntil > DateTime.UtcNow, $"The lease ended at {held.LeasedUntil:O}");

        _probe.Release.SetResult();

        Assert.Equal(XactJobStatus.Completed, (await WaitForHistoryAsync(running.Id)).Status);

        var stillWaiting = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == waiting.Id));
        Assert.Null(stillWaiting.Leaser);
    }

    [Fact]
    public async Task QuickPoll_AsksTheGate()
    {
        _gate.Open = false;

        // no regular poll during the test
        await CreateHostAsync(pollingInterval: 300);

        await _host!.StartAsync();

        var job = await WithScopeAsync(async sp =>
        {
            var quickPoll = sp.GetRequiredService<QuickPoll<TestDbContext>>();
            var job = quickPoll.JobEnqueue<GateJobs>(x => x.Run());
            await quickPoll.SaveChangesAndNotifyAsync(CancellationToken.None);
            return job;
        });

        await WaitForAsync(() => _gate.Calls.Count >= 1, "the gate's call");

        _gate.Open = true;

        await WithScopeAsync(sp =>
        {
            sp.GetRequiredService<QuickPoll<TestDbContext>>().Notify(QueueNames.Default);
            return Task.FromResult(0);
        });

        Assert.Equal(XactJobStatus.Completed, (await WaitForHistoryAsync(job.Id)).Status);
    }

    [Fact]
    public async Task IsolatedQueue_IsGatedToo_WithItsName()
    {
        _gate.Open = false;

        await CreateHostAsync(configure: o => o.WithIsolatedQueue("reports", q => q.WithWorkerCount(1).WithPollingInterval(1)));

        var job = await EnqueueAsync(db => db.JobEnqueue<GateJobs>(x => x.Run(), "reports"));

        await _host!.StartAsync();

        await WaitForAsync(() => _gate.Calls.Contains("reports"), "the reports queue's call");

        Assert.Null((await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == job.Id))).Leaser);

        _gate.Open = true;

        Assert.Equal(XactJobStatus.Completed, (await WaitForHistoryAsync(job.Id)).Status);
    }

    [Fact]
    public void GateTypeThatIsntOne_IsRefusedByAddXactJobs()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddXactJobs<TestDbContext>(options =>
        {
            options.Options.WorkerGate = typeof(Probe);
        }));

        Assert.Contains($"{typeof(Probe).FullName} doesn't implement IXactJobsWorkerGate", ex.Message);
    }

    [Fact]
    public void GateTheAppRegistered_IsKept()
    {
        var services = new ServiceCollection();

        services.AddSingleton(_gate);
        services.AddXactJobs<TestDbContext>(options => options.WithWorkerGate<Gate>());

        Assert.Single(services, d => d.ServiceType == typeof(Gate));
    }

    private async Task CreateHostAsync(int leaseSeconds = 120, int pollingInterval = 1, Action<XactJobsOptionsBuilder<TestDbContext>>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString));

        // the app's own instance: WithWorkerGate keeps it
        builder.Services.AddSingleton(_gate);
        builder.Services.AddSingleton(_probe);

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options
                .WithWorkerCount(1)
                .WithPollingInterval(pollingInterval)
                .WithLeaseDuration(leaseSeconds)
                .WithWorkerGate<Gate>();

            configure?.Invoke(options);
        });

        _host = builder.Build();

        await WithDbAsync(db => db.Database.EnsureCreatedAsync());
    }

    private Task<XactJob> EnqueueAsync(Func<TestDbContext, XactJob> enqueue)
    {
        return WithDbAsync(async db =>
        {
            var job = enqueue(db);
            await db.SaveChangesAsync();
            return job;
        });
    }

    private Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        return WithScopeAsync(sp => action(sp.GetRequiredService<TestDbContext>()));
    }

    private async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using var scope = _host!.Services.CreateScope();

        return await action(scope.ServiceProvider);
    }

    private async Task<XactJobHistory> WaitForHistoryAsync(long jobId)
    {
        XactJobHistory? history = null;

        await WaitForAsync(() =>
        {
            using var scope = _host!.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

            history = db.JobHistory.FirstOrDefault(h => h.Id == jobId);

            return history != null;
        }, $"the history of job {jobId}");

        return history!;
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

    public sealed class Gate : IXactJobsWorkerGate
    {
        private volatile bool _open = true;
        private volatile bool _throw;
        private readonly ConcurrentQueue<string> _calls = new();

        public bool Open { get => _open; set => _open = value; }

        public bool Throw { get => _throw; set => _throw = value; }

        public List<string> Calls => [.. _calls];

        public ValueTask<bool> CanClaimAsync(string queueName, CancellationToken cancellationToken)
        {
            _calls.Enqueue(queueName);

            if (_throw) throw new InvalidOperationException("The node's state couldn't be read");

            return ValueTask.FromResult(_open);
        }
    }

    public sealed class Probe
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public class GateJobs(Probe probe)
    {
        public void Run()
        {
        }

        public async Task Block()
        {
            probe.Started.TrySetResult();

            await probe.Release.Task;
        }
    }

    private sealed class LogCollector : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public int Count(string text) => _messages.Count(m => m.Contains(text));

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _messages.Enqueue(formatter(state, exception));
        }

        public void Dispose()
        {
        }
    }
}
