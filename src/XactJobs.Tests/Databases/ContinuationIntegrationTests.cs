using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XactJobs.Tests.Postgres;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.Tests.Databases;

// The class names start with the database, so a filter like "XactJobs.Tests.Databases.Oracle" runs all the suites of one database.

[Collection(OfficialMySqlCollection.Name)]
public sealed class OfficialMySqlContinuationTests(OfficialMySqlFixture fixture) : ContinuationIntegrationTests(fixture);

#if !NET10_0_OR_GREATER // Pomelo doesn't support EF Core 10 yet
[Collection(PomeloMySqlCollection.Name)]
public sealed class PomeloMySqlContinuationTests(PomeloMySqlFixture fixture) : ContinuationIntegrationTests(fixture);
#endif

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerContinuationTests(SqlServerFixture fixture) : ContinuationIntegrationTests(fixture);

[Collection(OracleCollection.Name)]
public sealed class OracleContinuationTests(OracleFixture fixture) : ContinuationIntegrationTests(fixture);

[Collection(PostgresCollection.Name)]
public sealed class PostgresContinuationTests(PostgresFixture fixture) : ContinuationIntegrationTests(fixture);

/// <summary>
/// Batches and continuations on every database: the claim of the continuations is SQL of each dialect,
/// and the batch ids are Guids, which every database stores in its own way.
/// </summary>
public abstract class ContinuationIntegrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string NotPolled = "not-polled";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(90);

    private readonly List<IHost> _hosts = [];
    private readonly ContinuationJobState _state = new();
    private readonly LogCapture _logs = new();

    public async Task InitializeAsync()
    {
        if (DatabaseFixture.IsDockerAvailable)
        {
            await fixture.PrepareForTestAsync();
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

    /// <summary>
    /// The claim saves the claim time as the continuation's original time, and a second claim, after the lease expired, keeps it.
    /// PostgreSQL, SQL Server and SQLite return the rows after the lease, so they have it at once. MySQL and Oracle return them
    /// as they were before this claim's lease: null on the first claim (the worker then uses its claim time, the value it saved),
    /// and the saved value on the second one, which reads the first claim's committed rows.
    /// </summary>
    [DockerFact]
    public async Task Claim_SavesTheClaimTime_AndASecondClaimAfterTheLeaseExpiredKeepsIt()
    {
        var host = CreateHost();

        var continuation = await WithDbAsync(host, async db =>
        {
            var continuation = NewContinuation("q", Guid.NewGuid());

            db.Jobs.Add(continuation);

            await db.SaveChangesAsync();

            return continuation;
        });

        var claimedA = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc).AddTicks(1234560);
        var claimedB = claimedA.AddSeconds(5);

        var first = Assert.Single(await ClaimContinuationsAsync(host, "q", 10, Guid.NewGuid(), claimedA));

        bool returnsRowsBeforeTheLease = fixture.Dialect is XactJobs.Internal.SqlDialects.MySqlDialect or XactJobs.Internal.SqlDialects.OracleDialect;

        Assert.Equal(returnsRowsBeforeTheLease ? null : claimedA, first.OriginallyScheduledAt);
        Assert.Equal(claimedA, await GetOriginallyScheduledAtAsync(host, continuation.Id));

        await WithDbAsync(host, db => db.Jobs
            .Where(j => j.Id == continuation.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeasedUntil, DateTime.UtcNow.AddMinutes(-1))));

        var second = Assert.Single(await ClaimContinuationsAsync(host, "q", 10, Guid.NewGuid(), claimedB));

        Assert.Equal(claimedA, second.OriginallyScheduledAt);
        Assert.Equal(claimedA, await GetOriginallyScheduledAtAsync(host, continuation.Id));
    }

    [DockerFact]
    public async Task Claim_TakesOnlyTheContinuationsOfFinishedBatches_OnTheQueue()
    {
        var host = CreateHost();

        var running = Guid.NewGuid();
        var finished = Guid.NewGuid();

        var expected = await WithDbAsync(host, async db =>
        {
            db.Jobs.Add(new XactJob(0, DateTime.UtcNow.AddHours(1), "T", "M()", "[]", "batch") { BatchId = running });

            var ready = NewContinuation("q", finished);

            db.Jobs.Add(ready);
            db.Jobs.Add(NewContinuation("q", running));
            db.Jobs.Add(NewContinuation("other", finished));
            db.Jobs.Add(new XactJob(0, XactJobBase.AwaitingBatchScheduledAt, "T", "M()", "[]", "q", leasedUntil: DateTime.UtcNow.AddMinutes(5))
            {
                AwaitsBatchId = finished
            });

            await db.SaveChangesAsync();

            return ready.Id;
        });

        var leaser = Guid.NewGuid();

        var claimed = await ClaimContinuationsAsync(host, "q", 10, leaser);

        var job = Assert.Single(claimed);

        Assert.Equal(expected, job.Id);
        Assert.Equal(finished, job.AwaitsBatchId);
        Assert.Equal(XactJobContinuation.OnFinish, job.ContinuationCondition);
        Assert.Equal(XactJobBase.AwaitingBatchScheduledAt, job.ScheduledAt);

        // MySQL and Oracle return the rows as they were locked, before the lease
        // (and on Oracle, the lease SQL writes the leaser in another byte order than EF reads it)
        Assert.NotNull(await WithDbAsync(host, db => db.Jobs.Where(j => j.Id == expected).Select(j => j.Leaser).SingleAsync()));

        // leased now
        Assert.Empty(await ClaimContinuationsAsync(host, "q", 10, Guid.NewGuid()));
    }

    [DockerFact]
    public async Task BatchWithAFailedJob_RunsTheContinuationsWhoseConditionIsMet()
    {
        var host = CreateHost();

        var (batch, onSuccess, onFailure, onFinish, afterOnFailure) = await WithDbAsync(host, async db =>
        {
            var batch = db.JobBatch()
                .Enqueue<ContinuationJob>(x => x.Count("a"))
                .Enqueue<ContinuationJob>(x => x.Cancel("b"))
                .Enqueue<ContinuationJob>(x => x.CountAsync("c"));

            var onFailure = batch.ContinueWith<ContinuationJob>(x => x.Count("on failure"), XactJobContinuation.OnFailure);

            var result = (batch,
                          batch.ContinueWith<ContinuationJob>(x => x.Count("on success")),
                          onFailure,
                          batch.ContinueWith<ContinuationJob>(x => x.CountAsync("on finish"), XactJobContinuation.OnFinish),
                          onFailure.ContinueWith<ContinuationJob>(x => x.Count("after on failure")));

            await db.SaveChangesAsync();

            return result;
        });

        await host.StartAsync();

        var last = await WaitForFinalHistoryAsync(host, afterOnFailure.Id);

        Assert.Equal(XactJobStatus.Completed, last.Status);
        Assert.Equal(onFailure.BatchId, last.AwaitsBatchId);

        Assert.Equal(XactJobStatus.Skipped, (await WaitForFinalHistoryAsync(host, onSuccess.Id)).Status);
        Assert.Equal(XactJobStatus.Completed, (await WaitForFinalHistoryAsync(host, onFailure.Id)).Status);
        Assert.Equal(XactJobStatus.Completed, (await WaitForFinalHistoryAsync(host, onFinish.Id)).Status);

        Assert.Equal(["a", "after on failure", "b", "c", "on failure", "on finish"], _state.Runs.Keys.Order());

        await using var db = CreateDb();

        var batchHistory = await db.JobHistory.AsNoTracking().Where(h => h.BatchId == batch.Id).ToListAsync();

        Assert.Equal(3, batchHistory.Count);
        Assert.Single(batchHistory, h => h.Status == XactJobStatus.Cancelled);

        var continuations = await db.JobHistory.AsNoTracking().Where(h => h.AwaitsBatchId == batch.Id).ToListAsync();

        Assert.Equal(3, continuations.Count);
        Assert.All(continuations, h => Assert.True(h.ScheduledAt < DateTime.UtcNow.AddMinutes(1), "The history got the reserved scheduled time"));
        Assert.All(continuations, h => Assert.True(h.ProcessedAt >= batchHistory.Max(b => b.ProcessedAt)));

        Assert.False(_logs.HasErrors, _logs.ToString());
    }

    [DockerFact]
    public async Task CompetingWorkers_RunEachContinuationExactlyOnce()
    {
        const int continuationCount = 40;

        var hosts = new[] { CreateHost(Competing), CreateHost(Competing) };

        var continuationIds = await WithDbAsync(hosts[0], async db =>
        {
            var batch = db.JobBatch();

            for (var i = 0; i < 5; i++)
            {
                var name = $"job {i}";

                batch.Enqueue<ContinuationJob>(x => x.Count(name));
            }

            var ids = new List<XactJob>();

            for (var i = 0; i < continuationCount; i++)
            {
                var name = $"continuation {i}";

                ids.Add(batch.ContinueWith<ContinuationJob>(x => x.Count(name)));
            }

            await db.SaveChangesAsync();

            return ids.Select(j => j.Id).ToList();
        });

        await Task.WhenAll(hosts.Select(h => h.StartAsync()));

        await WaitForAsync(() => WithDbAsync(hosts[0], async db => !await db.Jobs.AnyAsync(j => j.PeriodicJobId == null)), "all the jobs to finish");

        Assert.Equal(5 + continuationCount, _state.Runs.Count);
        Assert.All(_state.Runs.Values, runs => Assert.Equal(1, runs));

        await using var db = CreateDb();

        Assert.Equal(continuationIds.Order(), await db.JobHistory.Where(h => h.Status == XactJobStatus.Completed && continuationIds.Contains(h.Id)).Select(h => h.Id).OrderBy(id => id).ToListAsync());
        Assert.False(_logs.HasErrors, _logs.ToString());
    }

    [DockerFact]
    public async Task RetriedJobs_NeverLetTheContinuationStartEarly()
    {
        // workers on two nodes claim the continuation all the time, while the batch's jobs are replaced by their retries
        var hosts = new[] { CreateHost(Retrying), CreateHost(Retrying) };

        var (batch, continuation) = await WithDbAsync(hosts[0], async db =>
        {
            var batch = db.JobBatch();

            for (var i = 0; i < 4; i++)
            {
                var name = $"job {i}";

                batch.Enqueue<ContinuationJob>(x => x.FailTimes(name, 3));
            }

            var continuation = batch.ContinueWith<ContinuationJob>(x => x.Count("then"));

            await db.SaveChangesAsync();

            return (batch, continuation);
        });

        await Task.WhenAll(hosts.Select(h => h.StartAsync()));

        var history = await WaitForFinalHistoryAsync(hosts[0], continuation.Id);

        Assert.Equal(XactJobStatus.Completed, history.Status);

        await using var db = CreateDb();

        var batchHistory = await db.JobHistory.AsNoTracking().Where(h => h.BatchId == batch.Id).ToListAsync();

        Assert.Equal(4 * 4, batchHistory.Count);
        Assert.Equal(4, batchHistory.Count(h => h.Status == XactJobStatus.Completed));
        Assert.All(batchHistory, h => Assert.True(h.ProcessedAt <= history.ProcessedAt, "The continuation started before the batch was finished"));
    }

    [DockerFact]
    public async Task HistoryCleanup_KeepsTheHistoryOfBatchesThatContinuationsWaitFor()
    {
        var host = CreateHost();

        var waited = Guid.NewGuid();
        var finished = Guid.NewGuid();
        var old = DateTime.UtcNow.AddDays(-40);

        await WithDbAsync(host, db =>
        {
            db.JobBatch(waited).ContinueWith<ContinuationJob>(x => x.Count("then"), queue: NotPolled);

            db.JobHistory.Add(NewHistory(1001, old, waited));
            db.JobHistory.Add(NewHistory(1002, old, finished));
            db.JobHistory.Add(NewHistory(1003, old, null));
            db.JobHistory.Add(NewHistory(1004, DateTime.UtcNow, finished));

            return db.SaveChangesAsync();
        });

        await RunHistoryCleanupAsync(host);

        await using var db = CreateDb();

        Assert.Equal([1001, 1004], await db.JobHistory.Select(h => h.Id).OrderBy(id => id).ToListAsync());
    }

    [DockerFact]
    public async Task Dashboard_KeepsTheBatchOfAJobRunNow_AndCancelsAWaitingContinuation()
    {
        var host = CreateHost();

        var (batch, continuation) = await WithDbAsync(host, async db =>
        {
            var batch = db.JobBatch().ScheduleIn<ContinuationJob>(x => x.Count("a"), TimeSpan.FromHours(1), NotPolled);

            var continuation = batch.ContinueWith<ContinuationJob>(x => x.Count("then"), queue: NotPolled);

            await db.SaveChangesAsync();

            return (batch, continuation);
        });

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.RunJobNowAsync(KeyOf(batch.Jobs[0].Id)))).Status);
        Assert.Equal(ActionStatus.Conflict, (await WithServiceAsync(s => s.RunJobNowAsync(KeyOf(continuation.Id)))).Status);
        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.CancelJobAsync(KeyOf(continuation.Id), null))).Status);

        await using var db = CreateDb();

        var replaced = await db.Jobs.AsNoTracking().SingleAsync();

        Assert.NotEqual(batch.Jobs[0].Id, replaced.Id);
        Assert.Equal(batch.Id, replaced.BatchId);

        var cancelled = await db.JobHistory.AsNoTracking().SingleAsync();

        Assert.Equal(continuation.Id, cancelled.Id);
        Assert.Equal(XactJobStatus.Cancelled, cancelled.Status);
        Assert.Equal(batch.Id, cancelled.AwaitsBatchId);
        Assert.True(cancelled.ScheduledAt < DateTime.UtcNow.AddMinutes(1));
    }

    private static void Competing(XactJobsOptionsBuilder<TestDbContext> options) => options.WithWorkerCount(2).WithMaxDegreeOfParallelism(4);

    private static void Retrying(XactJobsOptionsBuilder<TestDbContext> options) => options.WithWorkerCount(2).WithRetryStrategy(new QuickRetry());

    private static XactJob NewContinuation(string queue, Guid awaitsBatchId)
    {
        return new XactJob(0, XactJobBase.AwaitingBatchScheduledAt, "T", "M()", "[]", queue)
        {
            AwaitsBatchId = awaitsBatchId,
            ContinuationCondition = XactJobContinuation.OnFinish
        };
    }

    private static XactJobHistory NewHistory(long id, DateTime processedAt, Guid? batchId)
    {
        return new XactJobHistory(id, processedAt, XactJobStatus.Completed, processedAt, "T", "M()", "[]", QueueNames.Default)
        {
            BatchId = batchId
        };
    }

    /// <summary>
    /// The maintenance job is internal: it's resolved and run like the periodic job that runs it.
    /// </summary>
    private static async Task RunHistoryCleanupAsync(IHost host)
    {
        var type = typeof(XactJob).Assembly.GetType("XactJobs.Internal.XactJobMaintenance`1", throwOnError: true)!
            .MakeGenericType(typeof(TestDbContext));

        using var scope = host.Services.CreateScope();

        var maintenance = scope.ServiceProvider.GetRequiredService(type);

        var cleanup = type.GetMethod("CleanupJobHistoryAsync", BindingFlags.Public | BindingFlags.Instance)!;

        await (Task)cleanup.Invoke(maintenance, [CancellationToken.None])!;
    }

    private IHost CreateHost(Action<XactJobsOptionsBuilder<TestDbContext>>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);

        builder.Services.AddDbContext<TestDbContext>(options => fixture.Configure(options, retryOnFailure: false));

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options
                .WithWorkerCount(1)
                .WithPollingInterval(1)
                .WithShutdownTimeout(0)
                .WithRetryStrategy(new DefaultRetryStrategy(maxAttempts: 1));

            configure?.Invoke(options);
        });

        builder.Services.AddSingleton(_state);
        builder.Services.AddScoped<ContinuationJob>();

        var host = builder.Build();

        _hosts.Add(host);

        return host;
    }

    private async Task<List<XactJob>> ClaimContinuationsAsync(IHost host, string queue, int maxJobs, Guid leaser, DateTime? claimedAt = null)
    {
        using var scope = host.Services.CreateScope();

        DbContext db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        return await fixture.Dialect.ClaimContinuationsAsync(db, queue, maxJobs, leaser, 60, claimedAt ?? DateTime.UtcNow, CancellationToken.None);
    }

    private static Task<DateTime?> GetOriginallyScheduledAtAsync(IHost host, long jobId)
    {
        return WithDbAsync(host, db => db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.OriginallyScheduledAt).SingleAsync());
    }

    private TestDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>();

        fixture.Configure(options, retryOnFailure: false);

        return new TestDbContext(options.Options);
    }

    /// <summary>
    /// The primary key of a pending job, as the dashboard has it from the job's row (the stored time, with the database's precision).
    /// </summary>
    private JobKey KeyOf(long id)
    {
        using var db = CreateDb();

        return JobKey.Of(db.Jobs.AsNoTracking().Single(j => j.Id == id));
    }

    private async Task<T> WithServiceAsync<T>(Func<IXactJobsUIService, Task<T>> action)
    {
        await using var db = CreateDb();

        return await action(new XactJobsUIService<TestDbContext>(db));
    }

    /// <summary>
    /// Waits for the history row of the job's attempt that isn't retried.
    /// </summary>
    private static async Task<XactJobHistory> WaitForFinalHistoryAsync(IHost host, long jobId)
    {
        XactJobHistory? history = null;

        await WaitForAsync(async () =>
        {
            history = await WithDbAsync(host, db => db.JobHistory.AsNoTracking().FirstOrDefaultAsync(h => h.Id == jobId && h.Status != XactJobStatus.Failed));

            return history != null;
        }, $"the history of job #{jobId}");

        return history!;
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

    public sealed class ContinuationJobState
    {
        public ConcurrentDictionary<string, int> Runs { get; } = new();

        public ConcurrentDictionary<string, int> Attempts { get; } = new();
    }

    public sealed class ContinuationJob(ContinuationJobState state)
    {
        public void Count(string name) => state.Runs.AddOrUpdate(name, 1, (_, runs) => runs + 1);

        public Task CountAsync(string name)
        {
            Count(name);

            return Task.CompletedTask;
        }

        public void Cancel(string name)
        {
            Count(name);

            throw new XactJobCancelException($"{name} can't succeed");
        }

        public void FailTimes(string name, int failures)
        {
            if (state.Attempts.AddOrUpdate(name, 1, (_, n) => n + 1) <= failures)
            {
                throw new InvalidOperationException($"{name} failed on purpose");
            }

            Count(name);
        }
    }

    private sealed class QuickRetry : IRetryStrategy
    {
        public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount) => newErrorCount < 10 ? DateTime.UtcNow.AddMilliseconds(100) : null;
    }

    private sealed class LogCapture : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        // the failures of the retried jobs are logged as errors on purpose
        public bool HasErrors => _entries.Any(e => e.Level >= LogLevel.Error && !e.Message.Contains("failed on purpose") && !e.Message.Contains("can't succeed"));

        public override string ToString() => string.Join(Environment.NewLine, _entries.Select(e => $"{e.Level}: {e.Message}"));

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
                _entries.Enqueue((logLevel, $"{formatter(state, exception)} {exception}"));
            }
        }
    }
}
