using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

/// <summary>
/// Job filters around a job's run, and failure handlers for jobs that failed for good.
/// </summary>
public class SqliteJobFilterTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private readonly Log _log = new();
    private readonly Behavior _behavior = new();

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private IHost? _host;

    public SqliteJobFilterTests()
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
    public async Task Filters_RunInOrderAroundTheJob_WithItsArgumentsAndScope()
    {
        await CreateHostAsync(o => o.WithJobFilter<OuterFilter>().WithJobFilter<TenantFilter>());

        var job = await EnqueueAndStartAsync(db => db.JobEnqueue<FilterJobs>(x => x.Run(42)));

        var history = await WaitForHistoryAsync(job.Id);

        Assert.Equal(XactJobStatus.Completed, history.Status);
        Assert.Equal(["outer before", "tenant before (attempt 1)", "job: tenant 42, same DbContext True", "tenant after", "outer after"], _log.Entries);
    }

    [Fact]
    public async Task FilterException_IsTheJobsFailure()
    {
        _behavior.Mode = FilterMode.Throw;

        await CreateHostAsync(o => o.WithJobFilter<TenantFilter>());

        var job = await EnqueueAndStartAsync(db => db.JobEnqueue<FilterJobs>(x => x.Run(42)));

        var history = await WaitForHistoryAsync(job.Id);

        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.Equal(TenantFilter.FailureMessage, history.ErrorMessage);
        Assert.DoesNotContain(_log.Entries, e => e.StartsWith("job"));
    }

    [Fact]
    public async Task FilterThatSwallowsTheJobsException_StillFailsTheAttempt()
    {
        _behavior.Mode = FilterMode.Swallow;

        await CreateHostAsync(o => o.WithJobFilter<TenantFilter>());

        var job = await EnqueueAndStartAsync(db => db.JobEnqueue<FilterJobs>(x => x.Fail()));

        var history = await WaitForHistoryAsync(job.Id);

        // the job's changes can't be saved as a completed job's
        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.Equal(FilterJobs.FailureMessage, history.ErrorMessage);
        Assert.Contains("swallowed: " + FilterJobs.FailureMessage, _log.Entries);
    }

    [Fact]
    public async Task FilterThatTurnsTheJobsExceptionIntoACancel_StopsTheRetries()
    {
        _behavior.Mode = FilterMode.Cancel;

        await CreateHostAsync(o => o.WithJobFilter<TenantFilter>().WithJobFailureHandler<RecordingHandler>(), retries: 10);

        var job = await EnqueueAndStartAsync(db => db.JobEnqueue<FilterJobs>(x => x.Fail()));

        var history = await WaitForHistoryAsync(job.Id);

        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.Equal(TenantFilter.CancelMessage, history.ErrorMessage);

        await WaitForAsync(() => _log.Entries.Any(e => e.StartsWith("failed")), "the failure handler");
        Assert.Contains(_log.Entries, e => e.StartsWith($"failed {job.Id}:") && e.Contains("cancelled by the job True"));
    }

    [Fact]
    public void FilterAndHandlerTypesThatArentOnes_AreRefusedByAddXactJobs()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() => services.AddXactJobs<TestDbContext>(options =>
        {
            options.Options.JobFilters.Add(typeof(Log));
            options.Options.JobFailureHandlers.Add(typeof(IXactJobFailureHandler));
        }));

        Assert.Contains($"{typeof(Log).FullName} doesn't implement IXactJobFilter", ex.Message);
        Assert.Contains($"{typeof(IXactJobFailureHandler).FullName} isn't a concrete class", ex.Message);
    }

    [Fact]
    public async Task FilterThatDoesntCallNext_CompletesTheJobWithoutRunningIt()
    {
        _behavior.Mode = FilterMode.SkipNext;

        await CreateHostAsync(o => o.WithJobFilter<TenantFilter>());

        var job = await EnqueueAndStartAsync(db => db.JobEnqueue<FilterJobs>(x => x.Run(42)));

        var history = await WaitForHistoryAsync(job.Id);

        Assert.Equal(XactJobStatus.Completed, history.Status);
        Assert.DoesNotContain(_log.Entries, e => e.StartsWith("job"));
    }

    [Fact]
    public async Task FilterThatCallsNextTwice_FailsTheJob_WhichRanOnce()
    {
        _behavior.Mode = FilterMode.NextTwice;

        await CreateHostAsync(o => o.WithJobFilter<TenantFilter>());

        var job = await EnqueueAndStartAsync(db => db.JobEnqueue<FilterJobs>(x => x.Run(42)));

        var history = await WaitForHistoryAsync(job.Id);

        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.Contains("more than once", history.ErrorMessage);
        Assert.Single(_log.Entries, e => e.StartsWith("job"));
    }

    [Fact]
    public async Task FailureHandlers_AreToldOnce_WhenTheJobIsOutOfRetries_AndOneThatThrowsDoesntStopTheNext()
    {
        await CreateHostAsync(o => o.WithJobFailureHandler<ThrowingHandler>().WithJobFailureHandler<RecordingHandler>());

        var job = await EnqueueAndStartAsync(db => db.JobEnqueue<FilterJobs>(x => x.Fail()));

        var history = await WaitForHistoryAsync(job.Id);
        Assert.Equal(XactJobStatus.Cancelled, history.Status);

        await WaitForAsync(() => _log.Entries.Any(e => e.StartsWith("failed")), "the failure handler");

        Assert.Contains("throwing handler", _log.Entries);
        Assert.Equal([$"failed {job.Id}: attempt 1, {FilterJobs.FailureMessage}, cancelled by the job False, history True"],
            _log.Entries.Where(e => e.StartsWith("failed")));
    }

    [Fact]
    public async Task FailureHandler_IsToldWhenTheJobCancelsItself()
    {
        await CreateHostAsync(o => o.WithJobFailureHandler<RecordingHandler>(), retries: 10);

        var job = await EnqueueAndStartAsync(db => db.JobEnqueue<FilterJobs>(x => x.Cancel()));

        await WaitForAsync(() => _log.Entries.Any(e => e.StartsWith("failed")), "the failure handler");

        Assert.Contains(_log.Entries, e => e.StartsWith($"failed {job.Id}: attempt 1,") && e.Contains("cancelled by the job True"));
    }

    [Fact]
    public async Task FailureHandler_IsntToldOfAnAttemptThatIsRetried()
    {
        await CreateHostAsync(o => o.WithJobFailureHandler<RecordingHandler>(), retries: 10);

        var job = await EnqueueAndStartAsync(db => db.JobEnqueue<FilterJobs>(x => x.FailWithLongRetry()));

        var history = await WaitForHistoryAsync(job.Id);
        Assert.Equal(XactJobStatus.Failed, history.Status);

        // a handler would be told right after the failure is saved
        await Task.Delay(1000);

        Assert.DoesNotContain(_log.Entries, e => e.StartsWith("failed"));
    }

    private async Task CreateHostAsync(Action<XactJobsOptionsBuilder<TestDbContext>> configure, int retries = 0)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString));

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options
                .WithWorkerCount(1)
                .WithPollingInterval(1)
                .WithRetryStrategy(new DefaultRetryStrategy(maxRetries: retries, retrySeconds: [3600]));

            configure(options);
        });

        builder.Services.AddSingleton(_log);
        builder.Services.AddSingleton(_behavior);
        builder.Services.AddScoped<TenantContext>();

        _host = builder.Build();

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        await db.Database.EnsureCreatedAsync();
    }

    private async Task<XactJob> EnqueueAndStartAsync(Func<TestDbContext, XactJob> enqueue)
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

    public sealed class Log
    {
        private readonly ConcurrentQueue<string> _entries = new();

        public List<string> Entries => [.. _entries];

        public void Add(string entry) => _entries.Enqueue(entry);
    }

    public enum FilterMode { Normal, Throw, Swallow, Cancel, SkipNext, NextTwice }

    public sealed class Behavior
    {
        public FilterMode Mode { get; set; }
    }

    public sealed class TenantContext
    {
        public int? TenantId { get; set; }

        public DbContext? FilterDbContext { get; set; }
    }

    public sealed class OuterFilter(Log log) : IXactJobFilter
    {
        public async Task InvokeAsync(XactJobExecutingContext context, Func<Task> next)
        {
            log.Add("outer before");
            await next();
            log.Add("outer after");
        }
    }

    /// <summary>Sets the tenant from the job's tenantId argument, as a multi-tenant app would.</summary>
    public sealed class TenantFilter(TenantContext tenant, Log log, Behavior behavior) : IXactJobFilter
    {
        public const string FailureMessage = "The filter failed";
        public const string CancelMessage = "The filter cancelled the job";

        public async Task InvokeAsync(XactJobExecutingContext context, Func<Task> next)
        {
            var parameters = context.Method.GetParameters();
            var index = Array.FindIndex(parameters, p => p.Name == "tenantId");

            tenant.TenantId = index < 0 ? null : (int?)context.Arguments[index];
            tenant.FilterDbContext = context.DbContext;

            log.Add($"tenant before (attempt {context.Attempt})");

            switch (behavior.Mode)
            {
                case FilterMode.Throw:
                    throw new InvalidOperationException(FailureMessage);

                case FilterMode.SkipNext:
                    return;

                case FilterMode.NextTwice:
                    await next();
                    await next();
                    return;

                case FilterMode.Cancel:
                    try
                    {
                        await next();
                    }
                    catch (InvalidOperationException ex)
                    {
                        throw new XactJobCancelException(CancelMessage, ex);
                    }
                    return;

                case FilterMode.Swallow:
                    try
                    {
                        await next();
                    }
                    catch (InvalidOperationException ex)
                    {
                        log.Add("swallowed: " + ex.Message);
                    }
                    return;
            }

            await next();

            log.Add("tenant after");
        }
    }

    public sealed class FilterJobs(TenantContext tenant, TestDbContext db, Log log)
    {
        public const string FailureMessage = "The job failed";

        public void Run(int tenantId) => log.Add($"job: tenant {tenant.TenantId}, same DbContext {ReferenceEquals(db, tenant.FilterDbContext)}");

        public void Fail() => throw new InvalidOperationException(FailureMessage);

        public void Cancel() => throw new XactJobCancelException("Nothing to do any more");

        [XactRetry(1, [3600])]
        public void FailWithLongRetry() => throw new InvalidOperationException(FailureMessage);
    }

    public sealed class ThrowingHandler(Log log) : IXactJobFailureHandler
    {
        public Task OnFailedAsync(XactJobFailedContext context, CancellationToken cancellationToken)
        {
            log.Add("throwing handler");

            throw new InvalidOperationException("The handler failed");
        }
    }

    public sealed class RecordingHandler(Log log) : IXactJobFailureHandler
    {
        public async Task OnFailedAsync(XactJobFailedContext context, CancellationToken cancellationToken)
        {
            // the failure is saved before the handlers are told
            var db = context.Services.GetRequiredService<TestDbContext>();
            var isInHistory = await db.JobHistory.AnyAsync(h => h.Id == context.Job.Id, cancellationToken);

            log.Add($"failed {context.Job.Id}: attempt {context.Attempt}, {context.Exception.Message}, cancelled by the job {context.IsCancelledByJob}, history {isInHistory}");
        }
    }
}
