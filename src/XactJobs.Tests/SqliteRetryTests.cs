using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

/// <summary>
/// The queue's retry strategy gets the attempt's exception, and a job method's [XactRetry] replaces the queue's strategy.
/// </summary>
public class SqliteRetryTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private readonly RecordingRetryStrategy _queueStrategy = new();

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private IHost? _host;

    public SqliteRetryTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
    }

    public async Task InitializeAsync()
    {
        await _memoryConnection.OpenAsync();

        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString));

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options
                .WithWorkerCount(1)
                .WithPollingInterval(1)
                .WithRetryStrategy(_queueStrategy);
        });

        _host = builder.Build();

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        await db.Database.EnsureCreatedAsync();
    }

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
    public async Task QueueStrategy_GetsTheAttemptsException()
    {
        var job = await EnqueueAndRunAsync(db => db.JobEnqueue<RetryJobs>(x => x.ThrowTimeout()));

        var history = await WaitForHistoryAsync(job.Id);

        Assert.Equal(XactJobStatus.Cancelled, history.Status);

        var call = Assert.Single(_queueStrategy.Calls);
        Assert.Equal(job.Id, call.JobId);
        Assert.Equal(1, call.NewErrorCount);
        Assert.IsType<TimeoutException>(call.Exception);
        Assert.Equal(RetryJobs.TimeoutMessage, call.Exception.Message);
    }

    [Fact]
    public async Task XactRetryOfNoRetries_CancelsAfterTheFirstFailure_WithoutTheQueueStrategy()
    {
        // the queue's strategy would retry it
        _queueStrategy.RetryAt = DateTime.UtcNow.AddHours(1);

        var job = await EnqueueAndRunAsync(db => db.JobEnqueue<RetryJobs>(x => x.FailWithoutRetries()));

        var history = await WaitForHistoryAsync(job.Id);

        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.Equal(1, history.ErrorCount);
        Assert.Empty(_queueStrategy.Calls);
        Assert.Empty(await PendingAsync(job.Id));
    }

    [Fact]
    public async Task XactRetryWithWaits_ReschedulesAfterTheFirstWait()
    {
        var job = await EnqueueAndRunAsync(db => db.JobEnqueue<RetryJobs>(x => x.FailWithWaits()));

        var failedAt = DateTime.UtcNow;

        var history = await WaitForHistoryAsync(job.Id);

        Assert.Equal(XactJobStatus.Failed, history.Status);
        Assert.Empty(_queueStrategy.Calls);

        var retry = Assert.Single(await PendingAsync(job.Id));
        Assert.Equal(1, retry.ErrorCount);
        Assert.Equal(failedAt.AddSeconds(RetryJobs.FirstWaitSeconds), retry.ScheduledAt, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task XactRetry_IsInheritedByAnOverride()
    {
        _queueStrategy.RetryAt = DateTime.UtcNow.AddHours(1);

        var job = await EnqueueAndRunAsync(db => db.JobEnqueue<DerivedRetryJobs>(x => x.FailVirtual()));

        var history = await WaitForHistoryAsync(job.Id);

        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.Empty(_queueStrategy.Calls);
    }

    [Fact]
    public void InvalidXactRetry_IsRefusedAtEnqueue()
    {
        using var scope = _host!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        var negativeRetries = Assert.Throws<NotSupportedException>(() => db.JobEnqueue<InvalidRetryJobs>(x => x.NegativeRetries()));
        Assert.Contains("[XactRetry]", negativeRetries.Message);

        var negativeWait = Assert.Throws<NotSupportedException>(() => db.JobEnqueue<InvalidRetryJobs>(x => x.NegativeWait()));
        Assert.Contains("[XactRetry]", negativeWait.Message);
    }

    [Fact]
    public async Task QueueStrategyThatThrows_TheDefaultStrategyRetriesTheJob()
    {
        _queueStrategy.Throw = true;

        var job = await EnqueueAndRunAsync(db => db.JobEnqueue<RetryJobs>(x => x.ThrowTimeout()));

        var failedAt = DateTime.UtcNow;

        var history = await WaitForHistoryAsync(job.Id);

        // recorded, not lost: the default strategy's first retry
        Assert.Equal(XactJobStatus.Failed, history.Status);
        Assert.Single(_queueStrategy.Calls);

        var retry = Assert.Single(await PendingAsync(job.Id));
        Assert.Equal(1, retry.ErrorCount);
        Assert.Equal(failedAt.AddSeconds(DefaultRetryStrategy.DefaultRetrySeconds[0]), retry.ScheduledAt, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void InvalidXactRetryOnAnOverride_IsRefusedAtEnqueue()
    {
        using var scope = _host!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // the expression has the base method, which has no attribute: the runner reads the override's
        var ex = Assert.Throws<NotSupportedException>(() => db.JobEnqueue<InvalidOverrideJobs>(x => x.Run()));
        Assert.Contains("[XactRetry]", ex.Message);
    }

    [Fact]
    public void InvalidXactRetryOfAPeriodicJob_IsRefusedByAddXactJobs()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddXactJobs<TestDbContext>(options => options
            .WithPeriodicJob<InvalidRetryJobs>(x => x.NegativeRetries(), "invalid-retry", CronExpressions.Hourly)));

        Assert.Contains("periodic job 'invalid-retry'", ex.Message);
    }

    [Fact]
    public async Task InvalidXactRetryOfAnEnqueuedJob_UsesTheQueueStrategy()
    {
        // as if the attribute became invalid after the job was enqueued, which refuses an invalid one; with arguments of every kind
        var type = typeof(InvalidRetryJobs);
        var method = type.GetMethod(nameof(InvalidRetryJobs.FailWithInvalidRetry))!;
        var job = new XactJob(0, DateTime.UtcNow, $"{type.FullName}, {type.Assembly.GetName().Name}",
                              Internal.XactJobSerializer.GetMethodSignature(method), "[1,{\"A\":[1,2]},\"x\"]", QueueNames.Default);

        await EnqueueAndRunAsync(db => db.Jobs.Add(job).Entity);

        var history = await WaitForHistoryAsync(job.Id);

        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.Equal(InvalidRetryJobs.FailureMessage, history.ErrorMessage);

        // the queue's strategy decided, after the job's own failure (its arguments were read)
        Assert.Single(_queueStrategy.Calls);
    }

    [Fact]
    public void JobOfAnInterfaceMethodDeclaredOnABaseInterface_IsEnqueued()
    {
        using var scope = _host!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // the runner's lookup doesn't find Run on IDerivedJobs: the check of [XactRetry] mustn't refuse the job for that
        db.JobEnqueue<IDerivedJobs>(x => x.Run());
    }

    [Fact]
    public void DefaultRetrySeconds_CantBeChanged()
    {
        Assert.IsNotType<int[]>(DefaultRetryStrategy.DefaultRetrySeconds);
        Assert.IsNotType<int[]>(new DefaultRetryStrategy(3, [10, 60]).RetrySeconds);
    }

    [Fact]
    public void DefaultRetryStrategy_WithoutWaits_UsesTheDefaultWaits()
    {
        var strategy = new DefaultRetryStrategy(maxRetries: 3, retrySeconds: []);

        Assert.Equal(DefaultRetryStrategy.DefaultRetrySeconds, strategy.RetrySeconds);

        var retry = XactRetryAttribute.ReadFrom(typeof(RetryJobs).GetMethod(nameof(RetryJobs.FailWithDefaultWaits))!);
        var attributeStrategy = Assert.IsType<DefaultRetryStrategy>(retry!.Strategy);
        Assert.Equal(3, attributeStrategy.MaxRetries);
        Assert.Equal(DefaultRetryStrategy.DefaultRetrySeconds, attributeStrategy.RetrySeconds);
    }

    [Fact]
    public void DefaultRetryStrategy_UsesTheLastWaitAfterTheList_AndStopsAfterMaxRetries()
    {
        var strategy = new DefaultRetryStrategy(maxRetries: 3, retrySeconds: [10, 60]);
        var job = new XactJob(1, DateTime.UtcNow, "T", "M()", "[]", QueueNames.Default);
        var error = new InvalidOperationException();

        Assert.Equal(DateTime.UtcNow.AddSeconds(10), strategy.GetRetryTimeUtc(job, 1, error)!.Value, TimeSpan.FromSeconds(5));
        Assert.Equal(DateTime.UtcNow.AddSeconds(60), strategy.GetRetryTimeUtc(job, 2, error)!.Value, TimeSpan.FromSeconds(5));
        Assert.Equal(DateTime.UtcNow.AddSeconds(60), strategy.GetRetryTimeUtc(job, 3, error)!.Value, TimeSpan.FromSeconds(5));
        Assert.Null(strategy.GetRetryTimeUtc(job, 4, error));
    }

    [Fact]
    public void DefaultRetryStrategy_RefusesNegativeRetriesAndWaits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultRetryStrategy(maxRetries: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DefaultRetryStrategy(maxRetries: 3, retrySeconds: [10, -1]));
    }

    private async Task<XactJob> EnqueueAndRunAsync(Func<TestDbContext, XactJob> enqueue)
    {
        XactJob job;

        using (var scope = _host!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

            job = enqueue(db);

            await db.SaveChangesAsync();
        }

        await _host.StartAsync();

        return job;
    }

    private async Task<List<XactJob>> PendingAsync(long jobId)
    {
        using var scope = _host!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        return await db.Jobs.Where(j => j.Id == jobId).ToListAsync();
    }

    private async Task<XactJobHistory> WaitForHistoryAsync(long jobId)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (true)
        {
            using (var scope = _host!.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

                var history = await db.JobHistory.Where(h => h.Id == jobId).FirstOrDefaultAsync();

                if (history != null) return history;
            }

            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for the history of job {jobId}");

            await Task.Delay(50);
        }
    }

    public sealed class RecordingRetryStrategy : IRetryStrategy
    {
        public ConcurrentQueue<(long JobId, int NewErrorCount, Exception Exception)> Calls { get; } = new();

        /// <summary>Null: no retries.</summary>
        public DateTime? RetryAt { get; set; }

        public bool Throw { get; set; }

        public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount, Exception exception)
        {
            Calls.Enqueue((job.Id, newErrorCount, exception));

            if (Throw) throw new InvalidOperationException("The strategy failed");

            return RetryAt;
        }
    }

    public class RetryJobs
    {
        public const string TimeoutMessage = "The call timed out";
        public const int FirstWaitSeconds = 3600;

        public void ThrowTimeout() => throw new TimeoutException(TimeoutMessage);

        [XactRetry(0)]
        public void FailWithoutRetries() => throw new InvalidOperationException("Failed on purpose");

        [XactRetry(2, [FirstWaitSeconds, 2 * FirstWaitSeconds])]
        public void FailWithWaits() => throw new InvalidOperationException("Failed on purpose");

        [XactRetry(3)]
        public void FailWithDefaultWaits() => throw new InvalidOperationException("Failed on purpose");

        [XactRetry(0)]
        public virtual void FailVirtual() => throw new InvalidOperationException("Failed on purpose");
    }

    public class DerivedRetryJobs : RetryJobs
    {
        public override void FailVirtual() => throw new InvalidOperationException("Failed in the override");
    }

    public class InvalidRetryJobs
    {
        [XactRetry(-1)]
        public void NegativeRetries() { }

        [XactRetry(3, [10, -1])]
        public void NegativeWait() { }

        public const string FailureMessage = "Failed with an invalid [XactRetry]";

        [XactRetry(3, [10, -1])]
        public void FailWithInvalidRetry(int id, Payload payload, string name) => throw new InvalidOperationException(FailureMessage);
    }

    public sealed record Payload(int[] A);

    public interface IBaseJobs
    {
        void Run();
    }

    public interface IDerivedJobs : IBaseJobs;

    public class BaseJobs
    {
        public virtual void Run() { }
    }

    public class InvalidOverrideJobs : BaseJobs
    {
        [XactRetry(-1)]
        public override void Run() { }
    }
}
