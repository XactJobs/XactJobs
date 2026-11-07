using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.Tests;

/// <summary>
/// XactJobContext.OriginallyScheduledAt and the column behind it, with the real runner. The scenarios run on every database:
/// see <c>Databases/OriginalScheduleIntegrationTests</c> and <see cref="SqliteOriginalScheduleTests"/>.
/// </summary>
public sealed class OriginalScheduleScenarios(Action<DbContextOptionsBuilder> configure) : IAsyncDisposable
{
    private const string NotPolled = "not-polled";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private readonly List<IHost> _hosts = [];
    private readonly ScheduleState _state = new();

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// The first attempt sees its scheduled time, and the retries see the same, while their own scheduled times change.
    /// </summary>
    public async Task OneOffJob_ItsRetriesSeeTheTimeOfTheFirstAttempt()
    {
        var host = CreateHost();

        var at = WholeSeconds(DateTime.UtcNow.AddMinutes(-1));

        await WithDbAsync(host, db =>
        {
            db.JobScheduleAt<ScheduleJob>(x => x.Run("one-off", 2, null!), at);

            return db.SaveChangesAsync();
        });

        await host.StartAsync();

        var history = await WaitForHistoryAsync(host, "one-off", rows => rows.Any(h => h.Status == XactJobStatus.Completed));

        var runs = _state.RunsOf("one-off");

        Assert.Equal(3, runs.Count);
        Assert.All(runs, run => Assert.Equal(at, run));
        Assert.All(runs, run => Assert.Equal(DateTimeKind.Utc, run.Kind));

        Assert.Equal([XactJobStatus.Failed, XactJobStatus.Failed, XactJobStatus.Completed], history.Select(h => h.Status));

        // the first attempt had its own time, the retries other times, and they keep the original one
        Assert.Equal(at, history[0].ScheduledAt);
        Assert.Null(history[0].OriginallyScheduledAt);
        Assert.All(history.Skip(1), h => Assert.True(h.ScheduledAt > at));
        Assert.NotEqual(history[1].ScheduledAt, history[2].ScheduledAt);
        Assert.All(history.Skip(1), h => Assert.Equal(at, h.OriginallyScheduledAt));
    }

    /// <summary>
    /// Each run of a periodic job sees its own cron slot, and the retries of a run see the slot of that run.
    /// Every run fails once.
    /// </summary>
    public async Task PeriodicJob_EachRunSeesItsSlot_AndItsRetriesTheSame()
    {
        var host = CreateHost();

        await WithDbAsync(host, db =>
        {
            db.JobEnsurePeriodic<ScheduleJob>(x => x.FailFirstAttemptOfEachRun(null!), "every_second", "* * * * * *");

            return db.SaveChangesAsync();
        });

        await host.StartAsync();

        await WaitForHistoryAsync(host, "every_second", rows => rows.Count(h => h.Status == XactJobStatus.Completed) >= 2);

        await host.StopAsync();

        var history = await WithDbAsync(host, db => db.JobHistory.AsNoTracking().Where(h => h.PeriodicJobId == "every_second").ToListAsync());

        var completed = history.Where(h => h.Status == XactJobStatus.Completed).ToList();

        Assert.True(completed.Count >= 2);

        foreach (var retry in completed)
        {
            var slot = Assert.NotNull(retry.OriginallyScheduledAt);

            AssertCronSlot(slot);
            Assert.True(retry.ScheduledAt > slot);

            // the first attempt of the run was the slot itself, and both attempts saw it
            var first = Assert.Single(history, h => h.Status == XactJobStatus.Failed && h.ScheduledAt == slot);

            Assert.Null(first.OriginallyScheduledAt);
            Assert.Equal(2, _state.RunsOf("every_second").Count(run => run == slot));
        }

        Assert.Equal(completed.Count, completed.Select(h => h.OriginallyScheduledAt).Distinct().Count());
    }

    /// <summary>
    /// A continuation waits at the reserved time, so its first attempt, and then its retries, see the time it was claimed.
    /// </summary>
    public async Task Continuation_ItsRetriesSeeTheTimeItsFirstAttemptWasClaimed()
    {
        var host = CreateHost();

        await WithDbAsync(host, db =>
        {
            db.JobBatch()
                .Enqueue<ScheduleJob>(x => x.Run("batch", 0, null!))
                .ContinueWith<ScheduleJob>(x => x.Run("continuation", 2, null!));

            return db.SaveChangesAsync();
        });

        var started = DateTime.UtcNow;

        await host.StartAsync();

        var history = await WaitForHistoryAsync(host, "continuation", rows => rows.Any(h => h.Status == XactJobStatus.Completed));

        var runs = _state.RunsOf("continuation");

        Assert.Equal(3, runs.Count);

        var claimedAt = runs[0];

        Assert.NotEqual(XactJobBase.AwaitingBatchScheduledAt, claimedAt);
        Assert.True(claimedAt > started.AddSeconds(-1) && claimedAt < DateTime.UtcNow);
        Assert.All(runs, run => Assert.Equal(claimedAt, run));

        // the history of the first attempt has the claim time as its scheduled time, and the retries keep it
        Assert.Equal(claimedAt, history[0].ScheduledAt);
        Assert.Null(history[0].OriginallyScheduledAt);
        Assert.All(history.Skip(1), h => Assert.Equal(claimedAt, h.OriginallyScheduledAt));
    }

    /// <summary>
    /// The first attempt of a continuation keeps the time of its first claim: when its lease expires while it runs,
    /// and another worker runs it again, both runs see the same time, and so build the same idempotency key.
    /// </summary>
    public async Task Continuation_RunAgainAfterItsLeaseExpired_SeesTheTimeOfTheFirstClaim()
    {
        // one job at a time, so that worker A can't claim the continuation again itself
        var workerA = CreateHost(options => options.WithMaxDegreeOfParallelism(1));

        var continuationId = await WithDbAsync(workerA, async db =>
        {
            var continuation = db.JobBatch()
                .Enqueue<ScheduleJob>(x => x.Run("lease-batch", 0, null!))
                .ContinueWith<ScheduleJob>(x => x.BlockAsync("lease-continuation", null!, CancellationToken.None));

            await db.SaveChangesAsync();

            return continuation.Id;
        });

        await workerA.StartAsync();

        await WaitForAsync(() => _state.RunsOf("lease-continuation").Count == 1, "worker A to run the continuation");

        // A's lease expires while the continuation still runs (A extends it only after 3/4 of its duration)
        await WithDbAsync(workerA, db => db.Jobs
            .Where(j => j.Id == continuationId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeasedUntil, DateTime.UtcNow.AddMinutes(-1))));

        var workerB = CreateHost();

        await workerB.StartAsync();

        await WaitForAsync(() => _state.RunsOf("lease-continuation").Count == 2, "worker B to run the continuation again");

        var runs = _state.RunsOf("lease-continuation");

        Assert.NotEqual(XactJobBase.AwaitingBatchScheduledAt, runs[0]);
        Assert.Equal(runs[0], runs[1]);

        // the time of A's claim is stored in the row
        var stored = await WithDbAsync(workerB, db => db.Jobs.AsNoTracking().Where(j => j.Id == continuationId).Select(j => j.OriginallyScheduledAt).SingleAsync());

        Assert.Equal(runs[0], stored);
    }

    /// <summary>
    /// The claims of regular jobs and periodic runs don't set the column: it stays null, or keeps the value of a retry.
    /// </summary>
    public async Task RegularAndPeriodicClaims_DontSetTheColumn()
    {
        var host = CreateHost(options => options.WithMaxDegreeOfParallelism(4));

        var at = WholeSeconds(DateTime.UtcNow.AddMinutes(-1));
        var original = WholeSeconds(DateTime.UtcNow.AddMinutes(-10));

        await WithDbAsync(host, db =>
        {
            var template = db.JobEnqueue<ScheduleJob>(x => x.BlockAsync("retry", null!, CancellationToken.None));

            db.ChangeTracker.Clear();

            db.Jobs.Add(new XactJob(0, at, template.TypeName, template.MethodName, template.MethodArgs, template.Queue, errorCount: 1) { OriginallyScheduledAt = original });

            db.JobScheduleAt<ScheduleJob>(x => x.BlockAsync("regular", null!, CancellationToken.None), at);

            db.JobEnsurePeriodic<ScheduleJob>(x => x.BlockAsync("periodic", null!, CancellationToken.None), "blocking_periodic", "* * * * * *");

            return db.SaveChangesAsync();
        });

        await host.StartAsync();

        await WaitForAsync(() => new[] { "regular", "retry", "periodic" }.All(name => _state.RunsOf(name).Count == 1), "the three jobs to run");

        // claimed and running
        var rows = await WithDbAsync(host, db => db.Jobs.AsNoTracking().Where(j => j.LeasedUntil > DateTime.UtcNow).ToListAsync());

        Assert.Equal(3, rows.Count);

        var regular = Assert.Single(rows, j => j.MethodArgs.StartsWith("[\"regular\""));
        var retry = Assert.Single(rows, j => j.MethodArgs.StartsWith("[\"retry\""));
        var periodic = Assert.Single(rows, j => j.PeriodicJobId == "blocking_periodic");

        Assert.Null(regular.OriginallyScheduledAt);
        Assert.Equal(original, retry.OriginallyScheduledAt);
        Assert.Null(periodic.OriginallyScheduledAt);

        Assert.Equal([at], _state.RunsOf("regular"));
        Assert.Equal([original], _state.RunsOf("retry"));
        Assert.Equal([periodic.ScheduledAt], _state.RunsOf("periodic"));
    }

    /// <summary>
    /// Run now and Trigger make a pending run due now, and the run keeps the time it was scheduled for.
    /// After the triggered run, the periodic job's next run is scheduled for the same cron slot again (the current behavior).
    /// </summary>
    public async Task RunNowAndTrigger_KeepTheOriginalTime()
    {
        var host = CreateHost();

        var at = WholeSeconds(DateTime.UtcNow.AddHours(1));

        var jobId = await WithDbAsync(host, async db =>
        {
            var job = db.JobScheduleAt<ScheduleJob>(x => x.Run("run-now", 0, null!), at);

            db.JobEnsurePeriodic<ScheduleJob>(x => x.Run("trigger", 0, null!), "daily_trigger", Cron.Daily());

            await db.SaveChangesAsync();

            return job.Id;
        });

        var slot = await WithDbAsync(host, db => db.Jobs.Where(j => j.PeriodicJobId == "daily_trigger").Select(j => j.ScheduledAt).SingleAsync());

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(host, s => s.RunJobNowAsync(KeyOf(host, jobId)))).Status);
        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(host, s => s.TriggerPeriodicJobAsync("daily_trigger"))).Status);

        await host.StartAsync();

        var runNow = Assert.Single(await WaitForHistoryAsync(host, "run-now", rows => rows.Count > 0));
        var triggered = Assert.Single(await WaitForHistoryAsync(host, "daily_trigger", rows => rows.Count > 0));

        Assert.Equal([at], _state.RunsOf("run-now"));
        Assert.Equal([slot], _state.RunsOf("trigger"));

        Assert.True(runNow.ScheduledAt < at);
        Assert.Equal(at, runNow.OriginallyScheduledAt);
        Assert.True(triggered.ScheduledAt < slot);
        Assert.Equal(slot, triggered.OriginallyScheduledAt);

        // the next run gets the next cron slot after the triggered run: the same one, so it sees the same time
        var next = await WithDbAsync(host, db => db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == "daily_trigger"));

        Assert.Equal(slot, next.ScheduledAt);
        Assert.Null(next.OriginallyScheduledAt);
    }

    /// <summary>
    /// The dashboard's Retry of a job from the history keeps the time it was originally scheduled for: the original time of
    /// a job that ran out of retries, or else the scheduled time of the attempt (for a continuation, the time it was claimed).
    /// </summary>
    public async Task DashboardRetry_KeepsTheOriginalTime()
    {
        var host = CreateHost();

        var original = WholeSeconds(DateTime.UtcNow.AddMinutes(-10));
        var lastAttempt = WholeSeconds(DateTime.UtcNow.AddMinutes(-5));
        var cancelledAt = WholeSeconds(DateTime.UtcNow.AddMinutes(-3));

        await WithDbAsync(host, db =>
        {
            var retried = db.JobEnqueue<ScheduleJob>(x => x.Run("retried", 0, null!));
            var cancelled = db.JobEnqueue<ScheduleJob>(x => x.Run("cancelled", 0, null!));

            db.ChangeTracker.Clear();

            // ran out of retries: its last attempt has the original time
            db.JobHistory.Add(new XactJobHistory(1001, lastAttempt, XactJobStatus.Cancelled, lastAttempt, retried.TypeName, retried.MethodName, retried.MethodArgs,
                                                 retried.Queue, errorCount: 3, errorMessage: "Failed", errorStackTrace: "at ...") { OriginallyScheduledAt = original });

            // cancelled before its first attempt
            db.JobHistory.Add(new XactJobHistory(1002, cancelledAt, XactJobStatus.Cancelled, cancelledAt, cancelled.TypeName, cancelled.MethodName, cancelled.MethodArgs,
                                                 cancelled.Queue, errorMessage: "Cancelled"));

            return db.SaveChangesAsync();
        });

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(host, s => s.RetryJobAsync(1001))).Status);
        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(host, s => s.RetryJobAsync(1002))).Status);

        await host.StartAsync();

        var retriedRun = (await WaitForHistoryAsync(host, "retried", rows => rows.Any(h => h.Status == XactJobStatus.Completed))).Last();
        var cancelledRun = (await WaitForHistoryAsync(host, "cancelled", rows => rows.Any(h => h.Status == XactJobStatus.Completed))).Last();

        Assert.Equal([original], _state.RunsOf("retried"));
        Assert.Equal([cancelledAt], _state.RunsOf("cancelled"));

        Assert.Equal(original, retriedRun.OriginallyScheduledAt);
        Assert.Equal(cancelledAt, cancelledRun.OriginallyScheduledAt);
    }

    /// <summary>
    /// The dashboard shows the original time when it differs from the scheduled time, and its Cancel keeps it in the history.
    /// </summary>
    public async Task Dashboard_ShowsTheOriginalTimeWhenItDiffers_AndCancelKeepsIt()
    {
        var host = CreateHost();

        var original = WholeSeconds(DateTime.UtcNow.AddMinutes(-5));
        var retryAt = WholeSeconds(DateTime.UtcNow.AddHours(1));

        var (retry, plain, same) = await WithDbAsync(host, async db =>
        {
            var retry = new XactJob(0, retryAt, "T", "M()", "[]", NotPolled, errorCount: 1) { OriginallyScheduledAt = original };
            var plain = new XactJob(0, retryAt, "T", "M()", "[]", NotPolled);
            var same = new XactJob(0, retryAt.AddSeconds(1), "T", "M()", "[]", NotPolled) { OriginallyScheduledAt = retryAt.AddSeconds(1) };

            db.Jobs.AddRange(retry, plain, same);

            await db.SaveChangesAsync();

            return (retry.Id, plain.Id, same.Id);
        });

        Assert.Equal(original, (await WithServiceAsync(host, s => s.GetJobAsync(KeyOf(host, retry))))!.OriginallyScheduledAt);
        Assert.Null((await WithServiceAsync(host, s => s.GetJobAsync(KeyOf(host, plain))))!.OriginallyScheduledAt);
        Assert.Null((await WithServiceAsync(host, s => s.GetJobAsync(KeyOf(host, same))))!.OriginallyScheduledAt);

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(host, s => s.CancelJobAsync(KeyOf(host, retry), null))).Status);
        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(host, s => s.CancelJobAsync(KeyOf(host, plain), null))).Status);

        var cancelled = await WithDbAsync(host, db => db.JobHistory.AsNoTracking().SingleAsync(h => h.Id == retry));

        Assert.Equal(original, cancelled.OriginallyScheduledAt);
        Assert.Equal(original, (await WithServiceAsync(host, s => s.GetJobHistoryByIdAsync(retry)))!.OriginallyScheduledAt);
        Assert.Null((await WithServiceAsync(host, s => s.GetJobHistoryByIdAsync(plain)))!.OriginallyScheduledAt);
    }

    /// <summary>
    /// A retry left by a worker of an older version has no original time: it sees its own scheduled time.
    /// </summary>
    public async Task RowWithoutOriginalTime_SeesItsScheduledTime()
    {
        var host = CreateHost();

        var retryAt = WholeSeconds(DateTime.UtcNow.AddSeconds(-30));

        await WithDbAsync(host, db =>
        {
            var template = db.JobEnqueue<ScheduleJob>(x => x.Run("old-retry", 0, null!));

            db.ChangeTracker.Clear();

            db.Jobs.Add(new XactJob(0, retryAt, template.TypeName, template.MethodName, template.MethodArgs, template.Queue, errorCount: 1));

            return db.SaveChangesAsync();
        });

        await host.StartAsync();

        var history = Assert.Single(await WaitForHistoryAsync(host, "old-retry", rows => rows.Count > 0));

        Assert.Equal([retryAt], _state.RunsOf("old-retry"));
        Assert.Null(history.OriginallyScheduledAt);
    }

    /// <summary>
    /// Like a CancellationToken, a context passed when the job is enqueued isn't stored: the worker passes its own.
    /// </summary>
    public async Task ContextPassedAtEnqueue_IsNotStored()
    {
        var host = CreateHost();

        var context = (XactJobContext)Activator.CreateInstance(typeof(XactJobContext), BindingFlags.NonPublic | BindingFlags.Instance, null,
                                                               [new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc)], null)!;

        var at = WholeSeconds(DateTime.UtcNow.AddMinutes(-2));

        var methodArgs = await WithDbAsync(host, async db =>
        {
            var job = db.JobScheduleAt<ScheduleJob>(x => x.Run("enqueued-context", 0, context), at);

            await db.SaveChangesAsync();

            return job.MethodArgs;
        });

        Assert.Equal("[\"enqueued-context\",0,null]", methodArgs);

        await host.StartAsync();

        await WaitForHistoryAsync(host, "enqueued-context", rows => rows.Count > 0);

        Assert.Equal([at], _state.RunsOf("enqueued-context"));
    }

    private IHost CreateHost(Action<XactJobsOptionsBuilder<TestDbContext>>? configureOptions = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<TestDbContext>(configure);

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options
                .WithWorkerCount(1)
                .WithPollingInterval(1)
                .WithShutdownTimeout(0)
                .WithRetryStrategy(new QuickRetry());

            configureOptions?.Invoke(options);
        });

        builder.Services.AddSingleton(_state);
        builder.Services.AddScoped<ScheduleJob>();

        var host = builder.Build();

        _hosts.Add(host);

        return host;
    }

    private static DateTime WholeSeconds(DateTime utc) => new(utc.Ticks - utc.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    private static void AssertCronSlot(DateTime slot)
    {
        Assert.True(slot.Ticks % TimeSpan.TicksPerSecond == 0, $"{slot:O} isn't a cron slot");
    }

    /// <summary>
    /// Waits for the history rows of a job, by its name (the first argument) or periodic job id, in the order they were processed.
    /// </summary>
    private static async Task<List<XactJobHistory>> WaitForHistoryAsync(IHost host, string name, Func<List<XactJobHistory>, bool> isDone)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (true)
        {
            var all = await WithDbAsync(host, db => db.JobHistory.AsNoTracking().ToListAsync());

            var rows = all
                .Where(h => h.PeriodicJobId == name || h.MethodArgs.StartsWith($"[\"{name}\""))
                .OrderBy(h => h.ProcessedAt)
                .ThenBy(h => h.Id)
                .ToList();

            if (isDone(rows)) return rows;

            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for the history of '{name}'");

            await Task.Delay(50);
        }
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

    /// <summary>
    /// The primary key of a pending job, as the dashboard has it from the job's row (the stored time, with the database's precision).
    /// </summary>
    private static JobKey KeyOf(IHost host, long id)
    {
        using var scope = host.Services.CreateScope();

        return JobKey.Of(scope.ServiceProvider.GetRequiredService<TestDbContext>().Jobs.AsNoTracking().Single(j => j.Id == id));
    }

    private static async Task<T> WithDbAsync<T>(IHost host, Func<TestDbContext, Task<T>> action)
    {
        using var scope = host.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    private static Task<T> WithServiceAsync<T>(IHost host, Func<IXactJobsUIService, Task<T>> action)
    {
        return WithDbAsync(host, db => action(new XactJobsUIService<TestDbContext>(db)));
    }

    public sealed class ScheduleState
    {
        private readonly ConcurrentQueue<(string Name, DateTime OriginallyScheduledAt)> _runs = new();
        private readonly ConcurrentDictionary<string, int> _attempts = new();

        public void Record(string name, DateTime originallyScheduledAt) => _runs.Enqueue((name, originallyScheduledAt));

        public int NextAttempt(string key) => _attempts.AddOrUpdate(key, 1, (_, attempts) => attempts + 1);

        public List<DateTime> RunsOf(string name) => _runs.Where(r => r.Name == name).Select(r => r.OriginallyScheduledAt).ToList();
    }

    public sealed class ScheduleJob(ScheduleState state)
    {
        /// <summary>
        /// Records the context, and fails the first <paramref name="failures"/> attempts.
        /// </summary>
        public void Run(string name, int failures, XactJobContext context)
        {
            state.Record(name, context.OriginallyScheduledAt);

            if (state.NextAttempt(name) <= failures)
            {
                throw new InvalidOperationException($"Attempt of '{name}' fails");
            }
        }

        /// <summary>
        /// Records the context, and runs until the worker stops.
        /// </summary>
        public async Task BlockAsync(string name, XactJobContext context, CancellationToken cancellationToken)
        {
            state.Record(name, context.OriginallyScheduledAt);

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public void FailFirstAttemptOfEachRun(XactJobContext context)
        {
            state.Record("every_second", context.OriginallyScheduledAt);

            if (state.NextAttempt($"every_second {context.OriginallyScheduledAt:O}") == 1)
            {
                throw new InvalidOperationException("The first attempt of the run fails");
            }
        }
    }

    private sealed class QuickRetry : IRetryStrategy
    {
        public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount) => newErrorCount < 10 ? DateTime.UtcNow.AddMilliseconds(100) : null;
    }
}
