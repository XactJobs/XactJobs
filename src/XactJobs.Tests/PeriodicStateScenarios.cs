using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.Tests;

/// <summary>
/// The state of a periodic job (XactJobContext.PeriodicState and SetPeriodicState), with the real runner. The scenarios run on
/// every database: see <c>Databases/PeriodicStateIntegrationTests</c> and <see cref="SqlitePeriodicStateTests"/>.
/// </summary>
public sealed class PeriodicStateScenarios(Action<DbContextOptionsBuilder> configure) : IAsyncDisposable
{
    private const string EverySecond = "* * * * * *";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private readonly List<IHost> _hosts = [];
    private readonly StateLog _log = new();

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// Each run gets the state the previous one set, and the history has each run's state before and after it.
    /// </summary>
    public async Task EachRun_GetsTheStateThePreviousRunSet()
    {
        var host = CreateHost();

        await EnsurePeriodicAsync(host, db => db.JobEnsurePeriodic<StateJob>(x => x.Count("count", null!), "count", EverySecond));

        await host.StartAsync();

        await WaitForHistoryAsync(host, "count", rows => rows.Count(h => h.Status == XactJobStatus.Completed) >= 3);

        await host.StopAsync();

        var history = await HistoryOfAsync(host, "count");

        Assert.All(history, h => Assert.Equal(XactJobStatus.Completed, h.Status));

        var expected = Enumerable.Range(0, history.Count).Select(i => (i == 0 ? null : $"{i}", (string?)$"{i + 1}"));

        Assert.Equal(expected, history.Select(h => (h.PreviousState, h.State)));
        Assert.Equal(history.Select(h => h.PreviousState), _log.SeenBy("count"));
        Assert.Equal($"{history.Count}", await StateOfAsync(host, "count"));
    }

    /// <summary>
    /// A failed attempt doesn't save the state it set: its retry gets the same state, and the history of the failed attempt
    /// has it before and after.
    /// </summary>
    public async Task FailedAttempt_LeavesTheState_AndItsRetryGetsTheSame()
    {
        var host = CreateHost();

        await EnsurePeriodicAsync(host, db => db.JobEnsurePeriodic<StateJob>(x => x.CountFailingFirstAttempt("failing", null!), "failing", EverySecond));

        await host.StartAsync();

        await WaitForHistoryAsync(host, "failing", rows => rows.Count(h => h.Status == XactJobStatus.Completed) >= 2);

        await host.StopAsync();

        var history = await HistoryOfAsync(host, "failing");

        // each run: a failed attempt, then its retry, which completes
        Assert.Equal(history.Select((_, i) => i % 2 == 0 ? XactJobStatus.Failed : XactJobStatus.Completed), history.Select(h => h.Status));

        for (var run = 0; run < history.Count / 2; run++)
        {
            var before = run == 0 ? null : $"{run}";

            Assert.Equal((before, before), (history[2 * run].PreviousState, history[2 * run].State));
            Assert.Equal((before, $"{run + 1}"), (history[2 * run + 1].PreviousState, history[2 * run + 1].State));
        }

        Assert.Equal(history.Select(h => h.PreviousState), _log.SeenBy("failing"));
    }

    /// <summary>
    /// A run that doesn't set the state keeps it, and null clears it.
    /// </summary>
    public async Task RunWithoutSet_KeepsTheState_AndNullClearsIt()
    {
        var host = CreateHost();

        await EnsurePeriodicAsync(host, db =>
        {
            db.JobEnsurePeriodic<StateJob>(x => x.Keep("keep", null!), "keep", EverySecond);
            db.JobEnsurePeriodic<StateJob>(x => x.Clear("clear", null!), "clear", EverySecond);
        });

        await SetStateAsync(host, "keep", "seed");
        await SetStateAsync(host, "clear", "seed");

        await host.StartAsync();

        await WaitForHistoryAsync(host, "keep", rows => rows.Count >= 2);
        await WaitForHistoryAsync(host, "clear", rows => rows.Count >= 2);

        await host.StopAsync();

        var kept = await HistoryOfAsync(host, "keep");
        var cleared = await HistoryOfAsync(host, "clear");

        Assert.All(kept, h => Assert.Equal(("seed", "seed"), (h.PreviousState, h.State)));
        Assert.Equal("seed", await StateOfAsync(host, "keep"));

        Assert.Equal(("seed", (string?)null), (cleared[0].PreviousState, cleared[0].State));
        Assert.All(cleared.Skip(1), h => Assert.Equal((null, null), (h.PreviousState, h.State)));
        Assert.Null(await StateOfAsync(host, "clear"));
    }

    /// <summary>
    /// The longest state is saved and read back as it was, in characters that take more than a byte; a longer one fails the
    /// attempt, which leaves the state as it was.
    /// </summary>
    public async Task LongestState_RoundTrips_AndALongerOneFailsTheAttempt()
    {
        var host = CreateHost(options => options.WithRetryStrategy(new NoRetry()));

        await EnsurePeriodicAsync(host, db =>
        {
            db.JobEnsurePeriodic<StateJob>(x => x.SetLong("longest", XactJobPeriodic.MaxStateLength, null!), "longest", EverySecond);
            db.JobEnsurePeriodic<StateJob>(x => x.SetLong("too_long", XactJobPeriodic.MaxStateLength + 1, null!), "too_long", EverySecond);
        });

        await SetStateAsync(host, "too_long", "seed");

        await host.StartAsync();

        await WaitForHistoryAsync(host, "longest", rows => rows.Count >= 2);
        await WaitForHistoryAsync(host, "too_long", rows => rows.Count >= 1);

        await host.StopAsync();

        var longest = new string('č', XactJobPeriodic.MaxStateLength);

        Assert.Equal(longest, await StateOfAsync(host, "longest"));
        Assert.Equal(longest, (await HistoryOfAsync(host, "longest"))[1].PreviousState);
        Assert.Equal(longest, _log.SeenBy("longest")[1]);

        var failed = (await HistoryOfAsync(host, "too_long"))[0];

        // no retries: the attempt ran out of them
        Assert.Equal(XactJobStatus.Cancelled, failed.Status);
        Assert.Contains($"{XactJobPeriodic.MaxStateLength} characters", failed.ErrorMessage);
        Assert.Equal(("seed", "seed"), (failed.PreviousState, failed.State));
        Assert.Equal("seed", await StateOfAsync(host, "too_long"));
    }

    /// <summary>
    /// A job that isn't a run of a periodic job has no state, and can't set one.
    /// </summary>
    public async Task OneOffJob_HasNoState_AndCantSetOne()
    {
        var host = CreateHost();

        await WithDbAsync(host, db =>
        {
            db.JobEnqueue<StateJob>(x => x.OneOff(null!));

            return db.SaveChangesAsync();
        });

        await host.StartAsync();

        await WaitForAsync(() => _log.SeenBy("one-off").Count == 1, "the one-off job");

        Assert.Equal("periodic job: (none), state: (none), set: InvalidOperationException", _log.SeenBy("one-off")[0]);

        var history = await WithDbAsync(host, db => db.JobHistory.AsNoTracking().SingleAsync());

        Assert.Equal((null, null), (history.PreviousState, history.State));
    }

    /// <summary>
    /// The state belongs to the periodic job's id: a change of its definition keeps it, and so does ensuring it again.
    /// </summary>
    public async Task DefinitionChange_KeepsTheState()
    {
        var host = CreateHost();

        await EnsurePeriodicAsync(host, db => db.JobEnsurePeriodic<StateJob>(x => x.Keep("defined", null!), "defined", Cron.Daily()));

        await SetStateAsync(host, "defined", "seed");

        await EnsurePeriodicAsync(host, db => db.JobEnsurePeriodic<StateJob>(x => x.Keep("defined", null!), "defined", Cron.Daily()));
        await EnsurePeriodicAsync(host, db => db.JobEnsurePeriodic<StateJob>(x => x.Keep("defined", null!), "defined", "0 0 * * * *"));

        var periodicJob = await WithDbAsync(host, db => db.PeriodicJobs.AsNoTracking().SingleAsync(p => p.Id == "defined"));

        Assert.Equal("0 0 * * * *", periodicJob.CronExpression);
        Assert.Equal("seed", periodicJob.State);
    }

    /// <summary>
    /// The dashboard shows the states, and doesn't retry a run that had one: as a one-off job, it would run without it.
    /// </summary>
    public async Task Dashboard_ShowsTheState_AndDoesntRetryARunWithOne()
    {
        var host = CreateHost();

        await EnsurePeriodicAsync(host, db => db.JobEnsurePeriodic<StateJob>(x => x.Keep("dashboard", null!), "dashboard", Cron.Daily()));

        await SetStateAsync(host, "dashboard", "seed");

        await WithDbAsync(host, db =>
        {
            var periodicJob = db.PeriodicJobs.AsNoTracking().Single(p => p.Id == "dashboard");

            XactJobHistory Run(long id, string? previousState, string? state) =>
                new(id, DateTime.UtcNow, XactJobStatus.Cancelled, DateTime.UtcNow, periodicJob.TypeName, periodicJob.MethodName, periodicJob.MethodArgs,
                    periodicJob.Queue, periodicJob.Id, periodicJob.CronExpression, periodicJob.Version, 3, "Failed", "at ...")
                {
                    PreviousState = previousState,
                    State = state
                };

            db.JobHistory.AddRange(Run(2001, "seed", "seed"), Run(2002, null, null));

            return db.SaveChangesAsync();
        });

        Assert.Equal("seed", (await WithServiceAsync(host, s => s.GetPeriodicJobByIdAsync("dashboard")))!.State);

        var withState = (await WithServiceAsync(host, s => s.GetJobHistoryByIdAsync(2001)))!;
        var withoutState = (await WithServiceAsync(host, s => s.GetJobHistoryByIdAsync(2002)))!;

        Assert.Equal(("seed", "seed"), (withState.PreviousState, withState.State));
        Assert.False(withState.CanRetry);
        Assert.True(withoutState.CanRetry);

        Assert.Equal(ActionStatus.Conflict, (await WithServiceAsync(host, s => s.RetryJobAsync(2001))).Status);
        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(host, s => s.RetryJobAsync(2002))).Status);
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

        builder.Services.AddSingleton(_log);
        builder.Services.AddScoped<StateJob>();

        var host = builder.Build();

        _hosts.Add(host);

        return host;
    }

    private static Task EnsurePeriodicAsync(IHost host, Action<TestDbContext> ensure)
    {
        return WithDbAsync(host, db =>
        {
            ensure(db);

            return db.SaveChangesAsync();
        });
    }

    /// <summary>
    /// As an app would, with SQL: the API has no way to set it outside a run.
    /// </summary>
    private static Task SetStateAsync(IHost host, string id, string? state)
    {
        return WithDbAsync(host, db => db.PeriodicJobs.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.State, state)));
    }

    private static Task<string?> StateOfAsync(IHost host, string id)
    {
        return WithDbAsync(host, db => db.PeriodicJobs.Where(p => p.Id == id).Select(p => p.State).SingleAsync());
    }

    /// <summary>
    /// The history rows of a periodic job, in the order they were processed.
    /// </summary>
    private static async Task<List<XactJobHistory>> HistoryOfAsync(IHost host, string id)
    {
        var rows = await WithDbAsync(host, db => db.JobHistory.AsNoTracking().Where(h => h.PeriodicJobId == id).ToListAsync());

        return rows.OrderBy(h => h.ProcessedAt).ThenBy(h => h.Id).ToList();
    }

    private static async Task WaitForHistoryAsync(IHost host, string id, Func<List<XactJobHistory>, bool> isDone)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (!isDone(await HistoryOfAsync(host, id)))
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for the history of '{id}'");

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

    private static async Task<T> WithDbAsync<T>(IHost host, Func<TestDbContext, Task<T>> action)
    {
        using var scope = host.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    private static Task<T> WithServiceAsync<T>(IHost host, Func<IXactJobsUIService, Task<T>> action)
    {
        return WithDbAsync(host, db => action(new XactJobsUIService<TestDbContext>(db)));
    }

    /// <summary>
    /// The state each attempt saw, by job name.
    /// </summary>
    public sealed class StateLog
    {
        private readonly ConcurrentQueue<(string Name, string? State)> _seen = new();
        private readonly ConcurrentDictionary<string, int> _attempts = new();

        public void Record(string name, string? state) => _seen.Enqueue((name, state));

        public int NextAttempt(string key) => _attempts.AddOrUpdate(key, 1, (_, attempts) => attempts + 1);

        public List<string?> SeenBy(string name) => _seen.Where(s => s.Name == name).Select(s => s.State).ToList();
    }

    public sealed class StateJob(StateLog log)
    {
        /// <summary>
        /// Counts its runs in the state.
        /// </summary>
        public void Count(string name, XactJobContext context)
        {
            log.Record(name, context.PeriodicState);

            context.SetPeriodicState($"{int.Parse(context.PeriodicState ?? "0") + 1}");
        }

        /// <summary>
        /// Counts its runs in the state, and the first attempt of each run fails after setting it.
        /// </summary>
        public void CountFailingFirstAttempt(string name, XactJobContext context)
        {
            log.Record(name, context.PeriodicState);

            context.SetPeriodicState($"{int.Parse(context.PeriodicState ?? "0") + 1}");

            if (log.NextAttempt($"{name} {context.OriginallyScheduledAt:O}") == 1)
            {
                throw new InvalidOperationException("The first attempt of the run fails");
            }
        }

        public void Keep(string name, XactJobContext context)
        {
            log.Record(name, context.PeriodicState);
        }

        public void Clear(string name, XactJobContext context)
        {
            log.Record(name, context.PeriodicState);

            context.SetPeriodicState(null);
        }

        public void SetLong(string name, int length, XactJobContext context)
        {
            log.Record(name, context.PeriodicState);

            context.SetPeriodicState(new string('č', length));
        }

        public void OneOff(XactJobContext context)
        {
            string set;

            try
            {
                context.SetPeriodicState("x");
                set = "done";
            }
            catch (Exception ex)
            {
                set = ex.GetType().Name;
            }

            log.Record("one-off", $"periodic job: {context.PeriodicJobId ?? "(none)"}, state: {context.PeriodicState ?? "(none)"}, set: {set}");
        }
    }

    private sealed class QuickRetry : IRetryStrategy
    {
        public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount) => newErrorCount < 10 ? DateTime.UtcNow.AddMilliseconds(100) : null;
    }

    private sealed class NoRetry : IRetryStrategy
    {
        public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount) => null;
    }
}
