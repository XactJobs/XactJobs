using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

/// <summary>
/// The check of the saves a job makes outside a transaction (<see cref="XactJobSaveCheck"/>), with the real runner. The scenarios run on every
/// database, because they depend on when each provider starts EF's own transaction: see <c>Databases/SaveCheckIntegrationTests</c> and
/// <see cref="SqliteSaveCheckTests"/>.
/// </summary>
public sealed class SaveCheckScenarios(Action<DbContextOptionsBuilder> configure) : IAsyncDisposable
{
    private const string NotPolled = "not-polled";
    private const string WarningText = "called SaveChanges outside a transaction";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private readonly List<IHost> _hosts = [];
    private readonly CapturingLoggerProvider _logs = new();

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    /// <summary>
    /// The save happens, the job completes, and the warning names the job. A second run of the same job method logs nothing more.
    /// </summary>
    public async Task SaveWithoutTransaction_Warns_OncePerJobMethod()
    {
        var host = CreateHost();

        await EnqueueAsync(host, db => db.JobEnqueue<SaveCheckJob>(x => x.SaveWithoutTransactionAsync("first", CancellationToken.None)));

        await host.StartAsync();

        await WaitForCompletedAsync(host, "first");

        var warning = Assert.Single(Warnings());

        Assert.Contains($"{typeof(SaveCheckJob).FullName}", warning);
        Assert.Contains(nameof(SaveCheckJob.SaveWithoutTransactionAsync), warning);
        Assert.Contains("https://github.com/XactJobs/XactJobs#exactly-once-database-effects", warning);

        await EnqueueAsync(host, db => db.JobEnqueue<SaveCheckJob>(x => x.SaveWithoutTransactionAsync("second", CancellationToken.None)));

        await WaitForCompletedAsync(host, "second");

        Assert.Single(Warnings());

        // a warning doesn't stop the save
        Assert.True(await HasMarkerAsync(host, "first"));
        Assert.True(await HasMarkerAsync(host, "second"));
    }

    public async Task SaveInsideTheJobsOwnTransaction_DoesntWarn()
    {
        var host = CreateHost();

        await EnqueueAsync(host, db => db.JobEnqueue<SaveCheckJob>(x => x.SaveInOwnTransactionAsync("own-transaction", CancellationToken.None)));

        await host.StartAsync();

        await WaitForCompletedAsync(host, "own-transaction");

        Assert.Empty(Warnings());
        Assert.True(await HasMarkerAsync(host, "own-transaction"));
    }

    /// <summary>
    /// The worker's own save, of the job's changes and its history row, runs outside any transaction of the job, and isn't checked.
    /// </summary>
    public async Task JobThatDoesntSave_DoesntWarn()
    {
        var host = CreateHost();

        await EnqueueAsync(host, db => db.JobEnqueue<SaveCheckJob>(x => x.LeaveToTheWorker("left-to-the-worker")));

        await host.StartAsync();

        await WaitForCompletedAsync(host, "left-to-the-worker");

        Assert.Empty(Warnings());
        Assert.True(await HasMarkerAsync(host, "left-to-the-worker"));
    }

    /// <summary>
    /// In Throw mode, the jobs that save the right way complete on their first attempt: so neither the worker's own save, nor a save inside the
    /// job's transaction, triggers the check.
    /// </summary>
    public async Task ThrowMode_JobsThatSaveTheRightWay_CompleteOnTheFirstAttempt()
    {
        var host = CreateHost(options => options.WithSaveOutsideTransaction(XactJobSaveCheck.Throw));

        await EnqueueAsync(host, db =>
        {
            db.JobEnqueue<SaveCheckJob>(x => x.LeaveToTheWorker("throw-mode-worker"));
            db.JobEnqueue<SaveCheckJob>(x => x.SaveInOwnTransactionAsync("throw-mode-transaction", CancellationToken.None));
        });

        await host.StartAsync();

        Assert.Equal(XactJobStatus.Completed, Assert.Single(await WaitForCompletedAsync(host, "throw-mode-worker")).Status);
        Assert.Equal(XactJobStatus.Completed, Assert.Single(await WaitForCompletedAsync(host, "throw-mode-transaction")).Status);

        Assert.True(await HasMarkerAsync(host, "throw-mode-worker"));
        Assert.True(await HasMarkerAsync(host, "throw-mode-transaction"));
    }

    /// <summary>
    /// In Throw mode, the save outside a transaction doesn't happen: the job fails with the error in the history, and is retried.
    /// </summary>
    public async Task ThrowMode_SaveWithoutTransaction_Fails_AndIsRetried()
    {
        var host = CreateHost(options => options.WithSaveOutsideTransaction(XactJobSaveCheck.Throw));

        await EnqueueAsync(host, db => db.JobEnqueue<SaveCheckJob>(x => x.SaveWithoutTransactionAsync("throw-mode", CancellationToken.None)));

        await host.StartAsync();

        var history = await WaitForHistoryAsync(host, "throw-mode", rows => rows.Count >= 2);

        await host.StopAsync();

        Assert.All(history, h =>
        {
            Assert.Equal(XactJobStatus.Failed, h.Status);
            Assert.Contains(WarningText, h.ErrorMessage);
            Assert.Contains(nameof(SaveCheckJob.SaveWithoutTransactionAsync), h.ErrorMessage);
        });

        // the second row is a retry
        Assert.True(history[1].ErrorCount > history[0].ErrorCount);

        Assert.False(await HasMarkerAsync(host, "throw-mode"));
        Assert.Empty(Warnings());
    }

    public async Task IgnoreMode_SaveWithoutTransaction_DoesntWarn()
    {
        var host = CreateHost(options => options.WithSaveOutsideTransaction(XactJobSaveCheck.Ignore));

        await EnqueueAsync(host, db => db.JobEnqueue<SaveCheckJob>(x => x.SaveWithoutTransactionAsync("ignore-mode", CancellationToken.None)));

        await host.StartAsync();

        await WaitForCompletedAsync(host, "ignore-mode");

        Assert.Empty(Warnings());
        Assert.True(await HasMarkerAsync(host, "ignore-mode"));
    }

    private List<string> Warnings() => _logs.Entries
        .Where(e => e.Level == LogLevel.Warning && e.Message.Contains(WarningText))
        .Select(e => e.Message)
        .ToList();

    private IHost CreateHost(Action<XactJobsOptionsBuilder<TestDbContext>>? configureOptions = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);

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

        builder.Services.AddScoped<SaveCheckJob>();

        var host = builder.Build();

        _hosts.Add(host);

        return host;
    }

    private static async Task EnqueueAsync(IHost host, Action<TestDbContext> enqueue)
    {
        using var scope = host.Services.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        enqueue(db);

        await db.SaveChangesAsync();
    }

    private static Task<List<XactJobHistory>> WaitForCompletedAsync(IHost host, string name)
    {
        return WaitForHistoryAsync(host, name, rows => rows.Any(h => h.Status == XactJobStatus.Completed));
    }

    /// <summary>
    /// Waits for the history rows of a job, by its name (the first argument), in the order they were processed.
    /// </summary>
    private static async Task<List<XactJobHistory>> WaitForHistoryAsync(IHost host, string name, Func<List<XactJobHistory>, bool> isDone)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (true)
        {
            var all = await WithDbAsync(host, db => db.JobHistory.AsNoTracking().ToListAsync());

            var rows = all
                .Where(h => h.MethodArgs.StartsWith($"[\"{name}\""))
                .OrderBy(h => h.ProcessedAt)
                .ThenBy(h => h.Id)
                .ToList();

            if (isDone(rows)) return rows;

            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for the history of '{name}'");

            await Task.Delay(50);
        }
    }

    private static async Task<bool> HasMarkerAsync(IHost host, string name)
    {
        var markers = await WithDbAsync(host, db => db.Jobs.AsNoTracking().Where(j => j.Queue == NotPolled).Select(j => j.MethodArgs).ToListAsync());

        return markers.Contains($"[\"{name}\"]");
    }

    private static async Task<T> WithDbAsync<T>(IHost host, Func<TestDbContext, Task<T>> action)
    {
        using var scope = host.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    public sealed class SaveCheckJob(TestDbContext db)
    {
        public void Marker(string name)
        {
        }

        public void LeaveToTheWorker(string name) => AddMarker(name);

        public async Task SaveWithoutTransactionAsync(string name, CancellationToken cancellationToken)
        {
            AddMarker(name);

            await db.SaveChangesAsync(cancellationToken);
        }

        public async Task SaveInOwnTransactionAsync(string name, CancellationToken cancellationToken)
        {
            await db.Database.BeginTransactionAsync(cancellationToken);

            AddMarker(name);

            await db.SaveChangesAsync(cancellationToken);
        }

        // a job row in a queue no worker polls: a write through the scoped DbContext, which the test can look for
        private void AddMarker(string name) => db.JobEnqueue<SaveCheckJob>(x => x.Marker(name), NotPolled);
    }

    private sealed class QuickRetry : IRetryStrategy
    {
        public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount) => newErrorCount < 10 ? DateTime.UtcNow.AddMilliseconds(100) : null;
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<(LogLevel Level, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                entries.Enqueue((logLevel, formatter(state, exception)));
            }
        }
    }
}
