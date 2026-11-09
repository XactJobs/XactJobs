using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

/// <summary>
/// The workers' metrics (the meter "XactJobs"), traces (the activity source "XactJobs") and the log scope of a job's attempt. The meter
/// and the source are shared with the other tests running at the same time, so each test has a queue of its own, and looks only at its
/// measurements and activities.
/// </summary>
public sealed class SqliteObservabilityTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    // the tests' own activities: the trace that enqueues a job, and what a job does inside its attempt
    internal static readonly ActivitySource TestSource = new("XactJobs.Tests");

    private readonly string _queue = $"obs-{Guid.NewGuid():N}";
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";
    private readonly SqliteConnection _memoryConnection;
    private readonly ScopeCapturingLoggerProvider _logs = new();

    private MetricsCollector? _metrics;
    private IHost? _host;

    public SqliteObservabilityTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
    }

    public Task InitializeAsync() => _memoryConnection.OpenAsync();

    public async Task DisposeAsync()
    {
        ObservabilityJobs.ReleaseAll();

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        _metrics?.Dispose();

        await _memoryConnection.DisposeAsync();
    }

    [Fact]
    public async Task Attempts_AreMeasuredByOutcome_WithTheirDurationAndStartDelay_AndTheClaims()
    {
        await StartAsync();

        // a job that completes, and one that fails twice: retried once, then out of retries
        await WithDbAsync(db =>
        {
            db.JobEnqueue(() => ObservabilityJobs.Succeed(), _queue);
            db.JobEnqueue(() => ObservabilityJobs.Fail(), _queue);
            return db.SaveChangesAsync();
        });

        await WaitForAsync(() => _metrics!.Sum("xactjobs.job.attempts") == 3, "the three attempts");

        Assert.Equal(1, _metrics!.Sum("xactjobs.job.attempts", outcome: "completed"));
        Assert.Equal(1, _metrics.Sum("xactjobs.job.attempts", outcome: "retried"));
        Assert.Equal(1, _metrics.Sum("xactjobs.job.attempts", outcome: "failed"));

        var durations = _metrics.Get("xactjobs.job.duration");
        Assert.Equal(3, durations.Count);
        Assert.All(durations, d => Assert.InRange(d.Value, 0, WaitTimeout.TotalSeconds));

        var delays = _metrics.Get("xactjobs.job.start_delay");
        Assert.Equal(3, delays.Count);
        Assert.All(delays, d => Assert.InRange(d.Value, 0, WaitTimeout.TotalSeconds));

        // the two jobs, then the retry
        Assert.Equal(3, _metrics.Sum("xactjobs.job.claimed"));
        Assert.NotEmpty(_metrics.Get("xactjobs.claim.duration"));

        // no job tags unless they're asked for
        Assert.All(_metrics.Get("xactjobs.job.attempts"), m => Assert.False(m.Tags.ContainsKey("xactjobs.job.type")));
    }

    [Fact]
    public async Task JobTags_AreAddedWhenAskedFor()
    {
        await StartAsync(jobTags: true);

        await WithDbAsync(db =>
        {
            db.JobEnqueue(() => ObservabilityJobs.Succeed(), _queue);
            return db.SaveChangesAsync();
        });

        await WaitForAsync(() => _metrics!.Sum("xactjobs.job.attempts") == 1, "the attempt");

        var attempt = Assert.Single(_metrics!.Get("xactjobs.job.attempts"));
        Assert.Equal(typeof(ObservabilityJobs).FullName, ((string)attempt.Tags["xactjobs.job.type"]!).Split(',')[0]);
        Assert.Equal("Succeed()", attempt.Tags["xactjobs.job.method"]);
    }

    [Fact]
    public async Task RunningAndHeldJobs_AreObservedPerQueue()
    {
        await StartAsync();

        var key = Guid.NewGuid().ToString("N");

        await WithDbAsync(db =>
        {
            db.JobEnqueue(() => ObservabilityJobs.BlockAsync(key), _queue);
            return db.SaveChangesAsync();
        });

        await WaitForAsync(() => ObservabilityJobs.IsBlocked(key), "the job to start");

        Assert.Equal(1, _metrics!.Observe("xactjobs.job.running"));
        Assert.Equal(1, _metrics.Observe("xactjobs.job.held"));

        ObservabilityJobs.Release(key);

        await WaitForAsync(() => _metrics.Observe("xactjobs.job.held") == 0, "the job to finish");

        Assert.Equal(0, _metrics.Observe("xactjobs.job.running"));
    }

    [Fact]
    public async Task WhatAJobLogs_HasTheJobsPropertiesInItsScope()
    {
        await StartAsync();

        var job = await WithDbAsync(async db =>
        {
            var enqueued = db.JobEnqueue<ObservabilityJobs>(x => x.Log("hello from the job"), _queue);
            await db.SaveChangesAsync();
            return enqueued;
        });

        await WaitForAsync(() => _logs.Entries.Any(e => e.Message == "hello from the job"), "the job's log entry");

        var scope = _logs.Entries.First(e => e.Message == "hello from the job").Scope;

        Assert.Equal(job.Id, scope["JobId"]);
        Assert.Equal(job.TypeName, scope["JobType"]);
        Assert.Equal("Log(System.String)", scope["JobMethod"]);
        Assert.Equal(_queue, scope["JobQueue"]);
        Assert.Equal(1, scope["JobAttempt"]);
    }

    [Fact]
    public async Task Attempts_AreTraced_AndLinkedToTheTraceThatEnqueuedThem()
    {
        using var activities = new ActivityCollector(_queue);

        await StartAsync();

        // enqueued in a trace (a request's, in an app): a job that completes, and one that fails twice
        string enqueueTraceParent;
        ActivityContext enqueueContext;

        using (var enqueue = TestSource.StartActivity("enqueue")!)
        {
            enqueueTraceParent = enqueue.Id!;
            enqueueContext = enqueue.Context;

            await WithDbAsync(db =>
            {
                db.JobEnqueue(() => ObservabilityJobs.Succeed(), _queue);
                db.JobEnqueue(() => ObservabilityJobs.Fail(), _queue);
                return db.SaveChangesAsync();
            });
        }

        await WaitForAsync(() => activities.Jobs.Count == 3, "the three attempts' activities");

        Assert.All(activities.Jobs, a =>
        {
            Assert.Equal(ActivityKind.Consumer, a.Kind);
            Assert.Null(a.Parent);
            Assert.Equal(default, a.ParentSpanId);

            var link = Assert.Single(a.Links);
            Assert.Equal(enqueueContext.TraceId, link.Context.TraceId);
            Assert.Equal(enqueueContext.SpanId, link.Context.SpanId);

            Assert.Equal("xactjobs", a.GetTagItem("messaging.system"));
            Assert.Equal("process", a.GetTagItem("messaging.operation.type"));
            Assert.Equal(_queue, a.GetTagItem("messaging.destination.name"));
        });

        var completed = Assert.Single(activities.Jobs, a => Equals(a.GetTagItem("xactjobs.outcome"), "completed"));
        Assert.Equal("ObservabilityJobs.Succeed", completed.DisplayName);
        Assert.Equal(ActivityStatusCode.Unset, completed.Status);

        var failures = activities.Jobs.Where(a => a.DisplayName == "ObservabilityJobs.Fail").OrderBy(a => a.GetTagItem("xactjobs.attempt")).ToList();
        Assert.Equal([1, 2], failures.Select(a => (int)a.GetTagItem("xactjobs.attempt")!));
        Assert.Equal(["retried", "failed"], failures.Select(a => (string)a.GetTagItem("xactjobs.outcome")!));
        Assert.All(failures, a =>
        {
            Assert.Equal(ActivityStatusCode.Error, a.Status);
            Assert.Equal("Failed on purpose", a.StatusDescription);
            Assert.Contains(a.Events, e => e.Name == "exception" && Equals(e.Tags.First(t => t.Key == "exception.message").Value, "Failed on purpose"));
        });

        // the retry kept the trace that enqueued the job, and the history keeps it
        var history = await WithDbAsync(db => db.JobHistory.AsNoTracking().Where(h => h.Queue == _queue).ToListAsync());
        Assert.Equal(3, history.Count);
        Assert.All(history, h => Assert.Equal(enqueueTraceParent, h.TraceParent));
    }

    [Fact]
    public async Task WhatAJobDoes_IsInsideItsActivity_AndAJobEnqueuedWithoutATraceHasNoLink()
    {
        using var activities = new ActivityCollector(_queue);

        await StartAsync();

        var key = Guid.NewGuid().ToString("N");

        var job = await WithDbAsync(async db =>
        {
            var enqueued = db.JobEnqueue(() => ObservabilityJobs.StartChildActivity(key), _queue);
            await db.SaveChangesAsync();
            return enqueued;
        });

        Assert.Null(job.TraceParent);

        await WaitForAsync(() => activities.Jobs.Count == 1 && activities.Children.Any(c => Equals(c.GetTagItem("key"), key)), "the job's activity and its child");

        var attempt = Assert.Single(activities.Jobs);
        var child = activities.Children.Single(c => Equals(c.GetTagItem("key"), key));

        Assert.Empty(attempt.Links);
        Assert.Equal(attempt.TraceId, child.TraceId);
        Assert.Equal(attempt.SpanId, child.ParentSpanId);
    }

    private async Task StartAsync(bool jobTags = false)
    {
        _metrics = new MetricsCollector(_queue);

        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString));
        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options.WithPollingInterval(1).WithWorkerCount(1);

            options.WithIsolatedQueue(_queue, queue => queue
                .WithPollingInterval(1)
                .WithWorkerCount(1)
                .WithRetryStrategy(new RetryOnceNow()));

            if (jobTags)
            {
                options.WithJobTagsInMetrics();
            }
        });

        _host = builder.Build();

        await WithDbAsync(db => db.Database.EnsureCreatedAsync());

        await _host.StartAsync();
    }

    private async Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        using var scope = _host!.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    private static async Task WaitForAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {description}");

            await Task.Delay(20);
        }
    }

    private sealed class RetryOnceNow : IRetryStrategy
    {
        public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount) => newErrorCount == 1 ? DateTime.UtcNow : null;
    }

    /// <summary>
    /// The stopped activities of the source "XactJobs" with the test's queue, and those of the tests' own source.
    /// </summary>
    private sealed class ActivityCollector : IDisposable
    {
        private readonly ActivityListener _listener;
        private readonly ConcurrentQueue<Activity> _jobs = new();
        private readonly ConcurrentQueue<Activity> _children = new();

        public ActivityCollector(string queue)
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name is "XactJobs" or "XactJobs.Tests",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.Source.Name == "XactJobs" && Equals(activity.GetTagItem("xactjobs.queue"), queue)) _jobs.Enqueue(activity);
                    else if (activity.Source.Name == "XactJobs.Tests") _children.Enqueue(activity);
                },
            };

            ActivitySource.AddActivityListener(_listener);
        }

        public List<Activity> Jobs => [.. _jobs];

        public List<Activity> Children => [.. _children];

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// The measurements of the meter "XactJobs" tagged with the test's queue.
    /// </summary>
    private sealed class MetricsCollector : IDisposable
    {
        private readonly string _queue;
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<(string Instrument, double Value, Dictionary<string, object?> Tags)> _measurements = new();

        public MetricsCollector(string queue)
        {
            _queue = queue;

            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == "XactJobs") listener.EnableMeasurementEvents(instrument);
            };

            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
            _listener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => Add(instrument, value, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));

            _listener.Start();
        }

        public List<(double Value, Dictionary<string, object?> Tags)> Get(string instrument) =>
            [.. _measurements.Where(m => m.Instrument == instrument).Select(m => (m.Value, m.Tags))];

        public double Sum(string instrument, string? outcome = null) =>
            Get(instrument).Where(m => outcome == null || Equals(m.Tags["xactjobs.outcome"], outcome)).Sum(m => m.Value);

        /// <summary>
        /// The current value of an observable instrument, for the queue.
        /// </summary>
        public double Observe(string instrument)
        {
            var before = _measurements.Count;

            _listener.RecordObservableInstruments();

            return _measurements.Skip(before).Where(m => m.Instrument == instrument).Select(m => m.Value).LastOrDefault();
        }

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var dictionary = new Dictionary<string, object?>();

            foreach (var tag in tags)
            {
                dictionary[tag.Key] = tag.Value;
            }

            if (Equals(dictionary.GetValueOrDefault("xactjobs.queue"), _queue))
            {
                _measurements.Enqueue((instrument.Name, value, dictionary));
            }
        }

        public void Dispose() => _listener.Dispose();
    }

    /// <summary>
    /// Records each entry's message with the properties of the scopes around it.
    /// </summary>
    private sealed class ScopeCapturingLoggerProvider : ILoggerProvider, ISupportExternalScope, ILogger
    {
        private IExternalScopeProvider? _scopes;

        public ConcurrentQueue<(string Message, Dictionary<string, object?> Scope)> Entries { get; } = new();

        public void SetScopeProvider(IExternalScopeProvider scopeProvider) => _scopes = scopeProvider;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => _scopes?.Push(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var scope = new Dictionary<string, object?>();

            _scopes?.ForEachScope((s, properties) =>
            {
                if (s is IEnumerable<KeyValuePair<string, object?>> pairs)
                {
                    foreach (var pair in pairs) properties[pair.Key] = pair.Value;
                }
            }, scope);

            Entries.Enqueue((formatter(state, exception), scope));
        }

        public void Dispose()
        {
        }
    }
}

public class ObservabilityJobs(ILogger<ObservabilityJobs> logger)
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource> Blocked = new();

    public static void Succeed()
    {
    }

    public static void Fail() => throw new InvalidOperationException("Failed on purpose");

    public static Task BlockAsync(string key) => Blocked.GetOrAdd(key, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task;

    public static bool IsBlocked(string key) => Blocked.ContainsKey(key);

    public static void Release(string key) => Blocked.GetOrAdd(key, _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

    public static void ReleaseAll()
    {
        foreach (var tcs in Blocked.Values) tcs.TrySetResult();
    }

    public void Log(string message) => logger.LogInformation("{Message}", message);

    public static void StartChildActivity(string key)
    {
        using var child = SqliteObservabilityTests.TestSource.StartActivity("child");

        child?.SetTag("key", key);
    }
}
