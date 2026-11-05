using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

public class SqliteContinuationTests : IAsyncLifetime
{
    private const string NotPolled = "not-polled";
    private const string Reports = "reports";
    private const string CleanupJobId = "xj_history_cleanup";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private readonly CallLog _log = new();

    private IHost? _host;

    public SqliteContinuationTests()
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
    public async Task OnSuccess_RunsAfterAllTheJobsOfTheBatchSucceeded()
    {
        await CreateHostAsync();

        var (batch, continuation) = await SaveAsync(db =>
        {
            var batch = db.JobBatch()
                .Enqueue<BatchJobs>(x => x.RecordSlowlyAsync("a", 300))
                .Enqueue<BatchJobs>(x => x.Record("b"))
                .ScheduleIn<BatchJobs>(x => x.Record("c"), TimeSpan.FromSeconds(1));

            return (batch, batch.ContinueWith<BatchJobs>(x => x.Record("then")));
        });

        await StartAsync();

        var history = await WaitForFinalHistoryAsync(continuation.Id);

        Assert.Equal(XactJobStatus.Completed, history.Status);
        Assert.Equal("then", _log.Calls.Last());
        Assert.Equal(["a", "b", "c"], _log.Calls.Take(3).Order());

        // the history links the continuation to its batch, and doesn't get the reserved scheduled time
        Assert.Equal(batch.Id, history.AwaitsBatchId);
        Assert.Equal(XactJobContinuation.OnSuccess, history.ContinuationCondition);
        Assert.Null(history.BatchId);
        Assert.True(history.ScheduledAt < DateTime.UtcNow.AddMinutes(1));

        var batchHistory = await WithDbAsync(db => db.JobHistory.Where(h => h.BatchId == batch.Id).ToListAsync());

        Assert.Equal(3, batchHistory.Count);
        Assert.All(batchHistory, h => Assert.True(h.ProcessedAt <= history.ProcessedAt));
    }

    [Fact]
    public async Task WaitingContinuation_IsScheduledAtTheReservedTime()
    {
        await CreateHostAsync();

        var (batch, continuation) = await SaveAsync(db =>
        {
            var batch = db.JobBatch().Enqueue<BatchJobs>(x => x.Record("a"), NotPolled);

            return (batch, batch.ContinueWith<BatchJobs>(x => x.Record("then")));
        });

        await StartAsync();

        await Task.Delay(1500);

        var row = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == continuation.Id));

        Assert.True(row.IsAwaitingBatch);
        Assert.Equal(XactJobBase.AwaitingBatchScheduledAt, row.ScheduledAt);
        Assert.Equal(batch.Id, row.AwaitsBatchId);
        Assert.Null(row.LeasedUntil);
        Assert.Empty(_log.Calls);
    }

    [Theory]
    [InlineData(XactJobContinuation.OnSuccess, false)]
    [InlineData(XactJobContinuation.OnFailure, true)]
    [InlineData(XactJobContinuation.OnFinish, true)]
    public async Task BatchWithAFailedJob_RunsTheContinuationIfItsConditionIsMet(XactJobContinuation when, bool runs)
    {
        await CreateHostAsync();

        var continuation = await SaveAsync(db => db.JobBatch()
            .Enqueue<BatchJobs>(x => x.Record("a"))
            .Enqueue<BatchJobs>(x => x.Cancel("b"))
            .ContinueWith<BatchJobs>(x => x.Record("then"), when));

        await StartAsync();

        var history = await WaitForFinalHistoryAsync(continuation.Id);

        if (runs)
        {
            Assert.Equal(XactJobStatus.Completed, history.Status);
            Assert.Contains("then", _log.Calls);
        }
        else
        {
            Assert.Equal(XactJobStatus.Skipped, history.Status);
            Assert.Contains("had 1 failed job(s)", history.ErrorMessage);
            Assert.DoesNotContain("then", _log.Calls);
        }
    }

    [Theory]
    [InlineData(XactJobContinuation.OnSuccess, true)]
    [InlineData(XactJobContinuation.OnFailure, false)]
    [InlineData(XactJobContinuation.OnFinish, true)]
    public async Task SuccessfulBatch_RunsTheContinuationIfItsConditionIsMet(XactJobContinuation when, bool runs)
    {
        await CreateHostAsync();

        var continuation = await SaveAsync(db => db.JobBatch()
            .Enqueue<BatchJobs>(x => x.Record("a"))
            .ContinueWith<BatchJobs>(x => x.Record("then"), when));

        await StartAsync();

        var history = await WaitForFinalHistoryAsync(continuation.Id);

        Assert.Equal(runs ? XactJobStatus.Completed : XactJobStatus.Skipped, history.Status);

        if (!runs)
        {
            Assert.Contains("had no failed jobs", history.ErrorMessage);
        }
    }

    [Fact]
    public async Task RetriedJob_StaysInTheBatch_AndTheContinuationWaitsForIt()
    {
        await CreateHostAsync();

        var (batch, continuation) = await SaveAsync(db =>
        {
            var batch = db.JobBatch().Enqueue<BatchJobs>(x => x.FailOnce("a"));

            return (batch, batch.ContinueWith<BatchJobs>(x => x.Record("then")));
        });

        await StartAsync();

        var history = await WaitForFinalHistoryAsync(continuation.Id);

        Assert.Equal(XactJobStatus.Completed, history.Status);
        Assert.Equal(["a", "then"], _log.Calls);

        var batchHistory = await WithDbAsync(db => db.JobHistory.Where(h => h.BatchId == batch.Id).OrderBy(h => h.ProcessedAt).ToListAsync());

        Assert.Equal([XactJobStatus.Failed, XactJobStatus.Completed], batchHistory.Select(h => h.Status));
    }

    [Fact]
    public async Task JobOutOfRetries_IsAFailureOfTheBatch()
    {
        await CreateHostAsync();

        var continuation = await SaveAsync(db => db.JobBatch()
            .Enqueue<BatchJobs>(x => x.AlwaysFail("a"))
            .ContinueWith<BatchJobs>(x => x.Record("then")));

        await StartAsync();

        var history = await WaitForFinalHistoryAsync(continuation.Id);

        Assert.Equal(XactJobStatus.Skipped, history.Status);
        Assert.Equal(QuickRetry.MaxAttempts, _log.AttemptsOf("a"));
    }

    [Fact]
    public async Task Chain_RunsInOrder()
    {
        await CreateHostAsync();

        var last = await SaveAsync(db => db.JobEnqueue<BatchJobs>(x => x.RecordSlowlyAsync("first", 200))
            .ContinueWith<BatchJobs>(x => x.RecordSlowlyAsync("second", 200))
            .ContinueWith<BatchJobs>(x => x.Record("third")));

        await StartAsync();

        var history = await WaitForFinalHistoryAsync(last.Id);

        Assert.Equal(XactJobStatus.Completed, history.Status);
        Assert.Equal(["first", "second", "third"], _log.Calls);
    }

    [Fact]
    public async Task SkippedContinuation_IsAFailureForTheNextOne()
    {
        await CreateHostAsync();

        var (skipped, afterSkipped, onFailure) = await SaveAsync(db =>
        {
            var skipped = db.JobEnqueue<BatchJobs>(x => x.Cancel("first"))
                .ContinueWith<BatchJobs>(x => x.Record("second"));

            return (skipped,
                    skipped.ContinueWith<BatchJobs>(x => x.Record("third")),
                    skipped.ContinueWith<BatchJobs>(x => x.Record("alert"), XactJobContinuation.OnFailure));
        });

        await StartAsync();

        Assert.Equal(XactJobStatus.Skipped, (await WaitForFinalHistoryAsync(skipped.Id)).Status);
        Assert.Equal(XactJobStatus.Skipped, (await WaitForFinalHistoryAsync(afterSkipped.Id)).Status);
        Assert.Equal(XactJobStatus.Completed, (await WaitForFinalHistoryAsync(onFailure.Id)).Status);
        Assert.Equal(["first", "alert"], _log.Calls);
    }

    [Fact]
    public async Task Job_CanHaveSeveralContinuations()
    {
        await CreateHostAsync();

        var (job, first, second) = await SaveAsync(db =>
        {
            var job = db.JobEnqueue<BatchJobs>(x => x.Record("job"));

            return (job, job.ContinueWith<BatchJobs>(x => x.Record("one")), job.ContinueWith<BatchJobs>(x => x.Record("two")));
        });

        Assert.NotNull(job.BatchId);
        Assert.Equal(job.BatchId, first.AwaitsBatchId);
        Assert.Equal(job.BatchId, second.AwaitsBatchId);

        await StartAsync();

        await WaitForFinalHistoryAsync(first.Id);
        await WaitForFinalHistoryAsync(second.Id);

        Assert.Equal("job", _log.Calls.First());
        Assert.Equal(["one", "two"], _log.Calls.Skip(1).Order());
    }

    [Fact]
    public async Task ContinuationAddedLater_ToAFinishedBatch_RunsRightAway()
    {
        await CreateHostAsync();

        var batch = await SaveAsync(db => db.JobBatch().Enqueue<BatchJobs>(x => x.Record("a")));

        await StartAsync();

        await WaitForFinalHistoryAsync(batch.Jobs[0].Id);

        // nothing wakes the queue up: the worker finds it on its regular check
        var continuation = await SaveAsync(db => db.JobBatch(batch.Id).ContinueWith<BatchJobs>(x => x.Record("later")));

        var history = await WaitForFinalHistoryAsync(continuation.Id);

        Assert.Equal(XactJobStatus.Completed, history.Status);
        Assert.Equal(["a", "later"], _log.Calls);
    }

    [Fact]
    public async Task ContinuationAddedLater_ToARunningBatch_WaitsForIt()
    {
        await CreateHostAsync();

        var batch = await SaveAsync(db => db.JobBatch().ScheduleIn<BatchJobs>(x => x.Record("a"), TimeSpan.FromSeconds(2)));

        var continuation = await SaveAsync(db => db.JobBatch(batch.Id).ContinueWith<BatchJobs>(x => x.Record("later")));

        await StartAsync();

        var history = await WaitForFinalHistoryAsync(continuation.Id);

        Assert.Equal(XactJobStatus.Completed, history.Status);
        Assert.Equal(["a", "later"], _log.Calls);
    }

    [Fact]
    public async Task UnknownBatch_IsFinished()
    {
        await CreateHostAsync();

        var continuation = await SaveAsync(db => db.JobBatch(Guid.NewGuid()).ContinueWith<BatchJobs>(x => x.Record("then")));

        await StartAsync();

        Assert.Equal(XactJobStatus.Completed, (await WaitForFinalHistoryAsync(continuation.Id)).Status);
    }

    [Fact]
    public async Task FinishedBatch_WakesUpTheContinuationsQueue()
    {
        // without the wake-up, the continuation would wait for the next regular check, in 30 seconds
        await CreateHostAsync(pollingIntervalInSeconds: 30);

        await StartAsync();

        var started = DateTime.UtcNow;

        var continuation = await WithDbAsync(async db =>
        {
            var quickPoll = new QuickPoll<TestDbContext>(db, _host!.Services.GetRequiredService<QuickPollChannels>());

            var continuation = quickPoll.JobBatch()
                .Enqueue<BatchJobs>(x => x.Record("a"))
                .ContinueWith<BatchJobs>(x => x.Record("then"), queue: Reports);

            await quickPoll.SaveChangesAndNotifyAsync(CancellationToken.None);

            return continuation;
        });

        var history = await WaitForFinalHistoryAsync(continuation.Id);

        Assert.Equal(XactJobStatus.Completed, history.Status);
        Assert.Equal(Reports, history.Queue);
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(15), "The continuation waited for the regular check");
    }

    [Fact]
    public async Task RetriedContinuation_IsStillAContinuation_ButDoesntWait()
    {
        await CreateHostAsync();

        var (batch, continuation) = await SaveAsync(db =>
        {
            var batch = db.JobBatch().Enqueue<BatchJobs>(x => x.Record("a"));

            return (batch, batch.ContinueWith<BatchJobs>(x => x.FailOnce("then")));
        });

        await StartAsync();

        var attempts = await WaitForHistoryAsync(db => db.JobHistory.Where(h => h.AwaitsBatchId == batch.Id), 2);

        Assert.Equal([XactJobStatus.Failed, XactJobStatus.Completed], attempts.OrderBy(h => h.ProcessedAt).Select(h => h.Status));
        Assert.Equal(continuation.Id, attempts.OrderBy(h => h.ProcessedAt).First().Id);
        Assert.All(attempts, h => Assert.True(h.ScheduledAt < DateTime.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public async Task HistoryCleanup_KeepsTheHistoryOfBatchesThatContinuationsWaitFor()
    {
        await CreateHostAsync();

        var waited = Guid.NewGuid();
        var finished = Guid.NewGuid();
        var old = DateTime.UtcNow.AddDays(-40);

        await WithDbAsync(async db =>
        {
            db.JobBatch(waited).ContinueWith<BatchJobs>(x => x.Record("then"), queue: NotPolled);

            db.JobHistory.Add(NewHistory(1001, old, waited));
            db.JobHistory.Add(NewHistory(1002, old, finished));
            db.JobHistory.Add(NewHistory(1003, old, null));
            db.JobHistory.Add(NewHistory(1004, DateTime.UtcNow, finished));

            return await db.SaveChangesAsync();
        });

        await StartAsync();

        // make the hourly cleanup due: the scheduled time isn't part of the key on Sqlite
        await WaitForAsync(() => WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == CleanupJobId)));
        await WithDbAsync(db => db.Jobs.Where(j => j.PeriodicJobId == CleanupJobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.ScheduledAt, DateTime.UtcNow.AddMinutes(-1))));

        await WaitForHistoryAsync(db => db.JobHistory.Where(h => h.PeriodicJobId == CleanupJobId), 1);

        Assert.Equal([1001, 1004], await WithDbAsync(db => db.JobHistory.Where(h => h.PeriodicJobId == null).Select(h => h.Id).OrderBy(id => id).ToListAsync()));
    }

    [Fact]
    public async Task SqlScripts_CreateASchemaForBatchesAndContinuations()
    {
        await CreateHostAsync(useSqlScripts: true);

        var continuation = await SaveAsync(db => db.JobBatch()
            .Enqueue<BatchJobs>(x => x.Record("a"))
            .Enqueue<BatchJobs>(x => x.Cancel("b"))
            .ContinueWith<BatchJobs>(x => x.Record("then"), XactJobContinuation.OnFailure));

        await StartAsync();

        Assert.Equal(XactJobStatus.Completed, (await WaitForFinalHistoryAsync(continuation.Id)).Status);
    }

    [Fact]
    public async Task SavedJob_CantBeContinued()
    {
        await CreateHostAsync();

        var job = await SaveAsync(db => db.JobEnqueue<BatchJobs>(x => x.Record("a"), NotPolled));

        var ex = Assert.Throws<InvalidOperationException>(() => job.ContinueWith<BatchJobs>(x => x.Record("then")));

        Assert.Contains("isn't saved yet", ex.Message);

        var loaded = await WithDbAsync(db => db.Jobs.SingleAsync(j => j.Id == job.Id));

        Assert.Throws<InvalidOperationException>(() => loaded.ContinueWith<BatchJobs>(x => x.Record("then")));
    }

    [Fact]
    public async Task JobOfABatch_CantBeContinuedOnItsOwn()
    {
        await CreateHostAsync();

        await WithDbAsync(db =>
        {
            var batch = db.JobBatch().Enqueue<BatchJobs>(x => x.Record("a"));

            var ex = Assert.Throws<InvalidOperationException>(() => batch.Jobs[0].ContinueWith<BatchJobs>(x => x.Record("then")));

            Assert.Contains("continue the batch instead", ex.Message);

            return Task.FromResult(0);
        });
    }

    [Fact]
    public async Task SavedBatch_CantGetMoreJobs()
    {
        await CreateHostAsync();

        var batch = await SaveAsync(db => db.JobBatch().Enqueue<BatchJobs>(x => x.Record("a"), NotPolled));

        Assert.Throws<InvalidOperationException>(() => batch.Enqueue<BatchJobs>(x => x.Record("b"), NotPolled));

        await WithDbAsync(db =>
        {
            Assert.Throws<InvalidOperationException>(() => db.JobBatch(batch.Id).Enqueue<BatchJobs>(x => x.Record("b"), NotPolled));

            return Task.FromResult(0);
        });
    }

    [Fact]
    public async Task NewBatchWithoutJobs_CantBeContinued()
    {
        await CreateHostAsync();

        await WithDbAsync(db =>
        {
            var ex = Assert.Throws<InvalidOperationException>(() => db.JobBatch().ContinueWith<BatchJobs>(x => x.Record("then")));

            Assert.Contains("Add the jobs of the batch before its continuations", ex.Message);

            Assert.Throws<ArgumentOutOfRangeException>(() => db.JobScheduleAt<BatchJobs>(x => x.Record("a"), XactJobBase.AwaitingBatchScheduledAt));

            Assert.Throws<ArgumentOutOfRangeException>(() => db.JobEnqueue<BatchJobs>(x => x.Record("a")).ContinueWith<BatchJobs>(x => x.Record("b"), (XactJobContinuation)42));

            return Task.FromResult(0);
        });
    }

    private static XactJobHistory NewHistory(long id, DateTime processedAt, Guid? batchId)
    {
        return new XactJobHistory(id, processedAt, XactJobStatus.Completed, processedAt, "T", "M()", "[]", QueueNames.Default)
        {
            BatchId = batchId
        };
    }

    private async Task CreateHostAsync(int pollingIntervalInSeconds = 1, bool useSqlScripts = false)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString));

        builder.Services.AddXactJobs<TestDbContext>(options => options
            .WithWorkerCount(1)
            .WithPollingInterval(pollingIntervalInSeconds)
            .WithRetryStrategy(new QuickRetry())
            .WithIsolatedQueue(Reports, q => q.WithWorkerCount(1).WithPollingInterval(pollingIntervalInSeconds)));

        builder.Services.AddSingleton(_log);
        builder.Services.AddScoped<BatchJobs>();

        _host = builder.Build();

        if (useSqlScripts)
        {
            foreach (var script in GetSqliteMigrationScripts())
            {
                await using var command = _memoryConnection.CreateCommand();

                command.CommandText = await File.ReadAllTextAsync(script);

                await command.ExecuteNonQueryAsync();
            }
        }
        else
        {
            await WithDbAsync(db => db.Database.EnsureCreatedAsync());
        }
    }

    private static IEnumerable<string> GetSqliteMigrationScripts()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "sql", "v0.1.0")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return new DirectoryInfo(Path.Combine(directory.FullName, "sql"))
            .GetDirectories("v*")
            .OrderBy(d => Version.Parse(d.Name[1..]))
            .Select(d => Path.Combine(d.FullName, $"xact-jobs-sqlite-{d.Name}.sql"))
            .Where(File.Exists);
    }

    private Task StartAsync() => _host!.StartAsync();

    private Task<T> SaveAsync<T>(Func<TestDbContext, T> add)
    {
        return WithDbAsync(async db =>
        {
            var result = add(db);

            await db.SaveChangesAsync();

            return result;
        });
    }

    /// <summary>
    /// Waits for the history row of the job's attempt that isn't retried.
    /// </summary>
    private async Task<XactJobHistory> WaitForFinalHistoryAsync(long jobId)
    {
        var rows = await WaitForHistoryAsync(db => db.JobHistory.Where(h => h.Id == jobId && h.Status != XactJobStatus.Failed), 1);

        return rows.Single();
    }

    private async Task<List<XactJobHistory>> WaitForHistoryAsync(Func<TestDbContext, IQueryable<XactJobHistory>> query, int count)
    {
        List<XactJobHistory> rows = [];

        await WaitForAsync(async () =>
        {
            rows = await WithDbAsync(db => query(db).AsNoTracking().ToListAsync());

            return rows.Count >= count;
        });

        return rows;
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting");

            await Task.Delay(50);
        }
    }

    private async Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        using var scope = _host!.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    public sealed class CallLog
    {
        private readonly ConcurrentQueue<string> _calls = new();
        private readonly ConcurrentDictionary<string, int> _attempts = new();

        public IReadOnlyList<string> Calls => _calls.ToArray();

        public void Add(string call) => _calls.Enqueue(call);

        public int Attempt(string name) => _attempts.AddOrUpdate(name, 1, (_, n) => n + 1);

        public int AttemptsOf(string name) => _attempts.GetValueOrDefault(name);
    }

    public sealed class BatchJobs(CallLog log)
    {
        public void Record(string name) => log.Add(name);

        public async Task RecordSlowlyAsync(string name, int delayMs)
        {
            await Task.Delay(delayMs);

            log.Add(name);
        }

        public void Cancel(string name)
        {
            log.Add(name);

            throw new XactJobCancelException($"{name} can't succeed");
        }

        public void FailOnce(string name)
        {
            if (log.Attempt(name) == 1) throw new InvalidOperationException($"{name} failed once");

            log.Add(name);
        }

        public void AlwaysFail(string name)
        {
            log.Attempt(name);

            throw new InvalidOperationException($"{name} always fails");
        }
    }

    private sealed class QuickRetry : IRetryStrategy
    {
        public const int MaxAttempts = 3;

        public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount)
        {
            return newErrorCount < MaxAttempts ? DateTime.UtcNow.AddMilliseconds(200) : null;
        }
    }
}
