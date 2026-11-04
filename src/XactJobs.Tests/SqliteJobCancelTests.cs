using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

public class SqliteJobCancelTests : IAsyncLifetime
{
    private const string Daily = "0 0 0 * * *";
    private const string NotPolled = "not-polled";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private IHost? _host;

    public SqliteJobCancelTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
    }

    public async Task InitializeAsync()
    {
        await _memoryConnection.OpenAsync();

        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString));

        // the default retry strategy retries a failed job several times
        builder.Services.AddXactJobs<TestDbContext>(options => options.WithWorkerCount(1).WithPollingInterval(1));

        builder.Services.AddScoped<CancellingJob>();

        _host = builder.Build();

        await WithDbAsync(db => db.Database.EnsureCreatedAsync());
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
    public async Task CancelledJob_MovesToTheHistoryAsCancelled_WithoutRetries()
    {
        var jobId = await EnqueueAsync(x => x.Cancel());

        var history = await RunAndGetHistoryAsync(jobId);

        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.Equal(1, history.ErrorCount);
        Assert.Equal(CancellingJob.Reason, history.ErrorMessage);
        Assert.Contains(nameof(CancellingJob.Cancel), history.ErrorStackTrace);
        Assert.False(await WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == null)));
    }

    [Fact]
    public async Task OtherFailures_AreStillRetried()
    {
        var jobId = await EnqueueAsync(x => x.Fail());

        var history = await RunAndGetHistoryAsync(jobId);

        Assert.Equal(XactJobStatus.Failed, history.Status);
        Assert.True(await WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == null && j.ErrorCount == 1)));
    }

    [Fact]
    public async Task WrappedCancel_IsFound_AndKeepsItsMessageAndCause()
    {
        var jobId = await EnqueueAsync(x => x.CancelWrapped());

        var history = await RunAndGetHistoryAsync(jobId);

        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.Equal(CancellingJob.Reason, history.ErrorMessage);
        Assert.Contains(CancellingJob.Cause, history.ErrorStackTrace);
    }

    [Fact]
    public async Task CancelInAnAggregateException_IsFound()
    {
        var jobId = await EnqueueAsync(x => x.CancelInAggregateAsync());

        var history = await RunAndGetHistoryAsync(jobId);

        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.Equal(CancellingJob.Reason, history.ErrorMessage);
    }

    [Fact]
    public async Task CancelledJob_LosesItsWrites()
    {
        var jobId = await EnqueueAsync(x => x.WriteAndCancel());

        var history = await RunAndGetHistoryAsync(jobId);

        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.False(await WithDbAsync(db => db.Jobs.AnyAsync(j => j.Queue == NotPolled)));
    }

    [Fact]
    public async Task CancelledPeriodicRun_IsFollowedByTheNextRun()
    {
        var jobId = await WithDbAsync(async db =>
        {
            db.JobEnsurePeriodic<CancellingJob>(x => x.Cancel(), "cancelling", Daily);

            await db.SaveChangesAsync();

            // make it due: the scheduled time isn't part of the key on Sqlite
            await db.Jobs.ExecuteUpdateAsync(s => s.SetProperty(j => j.ScheduledAt, DateTime.UtcNow.AddMinutes(-1)));

            return (await db.Jobs.AsNoTracking().SingleAsync()).Id;
        });

        var history = await RunAndGetHistoryAsync(jobId);

        Assert.Equal(XactJobStatus.Cancelled, history.Status);

        var next = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == "cancelling"));

        Assert.True(next.ScheduledAt > DateTime.UtcNow);
        Assert.Equal(0, next.ErrorCount);
        Assert.True((await WithDbAsync(db => db.PeriodicJobs.AsNoTracking().SingleAsync(p => p.Id == "cancelling"))).IsActive);
    }

    private Task<long> EnqueueAsync(System.Linq.Expressions.Expression<Action<CancellingJob>> expression)
    {
        return WithDbAsync(async db =>
        {
            var job = db.JobEnqueue(expression);

            await db.SaveChangesAsync();

            return job.Id;
        });
    }

    private Task<long> EnqueueAsync(System.Linq.Expressions.Expression<Func<CancellingJob, Task>> expression)
    {
        return WithDbAsync(async db =>
        {
            var job = db.JobEnqueue(expression);

            await db.SaveChangesAsync();

            return job.Id;
        });
    }

    private async Task<XactJobHistory> RunAndGetHistoryAsync(long jobId)
    {
        await _host!.StartAsync();

        var deadline = DateTime.UtcNow + WaitTimeout;

        while (!await WithDbAsync(db => db.JobHistory.AnyAsync(h => h.Id == jobId)))
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the history row");

            await Task.Delay(50);
        }

        return await WithDbAsync(db => db.JobHistory.AsNoTracking().SingleAsync(h => h.Id == jobId));
    }

    private async Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        using var scope = _host!.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    public sealed class CancellingJob(TestDbContext db)
    {
        public const string Reason = "Order 42 doesn't exist anymore";
        public const string Cause = "The order was deleted";

        public void Cancel()
        {
            throw new XactJobCancelException(Reason);
        }

        public void Fail()
        {
            throw new InvalidOperationException("Temporary failure");
        }

        public void CancelWrapped()
        {
            try
            {
                throw new XactJobCancelException(Reason, new KeyNotFoundException(Cause));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Wrapped by a library", ex);
            }
        }

        public async Task CancelInAggregateAsync()
        {
            await Task.Yield();

            throw new AggregateException(new TimeoutException("Unrelated"), new XactJobCancelException(Reason));
        }

        public void WriteAndCancel()
        {
            // not saved: the runner saves it with the job's row, unless the job fails
            db.JobEnqueue<CancellingJob>(x => x.Cancel(), NotPolled);

            throw new XactJobCancelException(Reason);
        }
    }
}
