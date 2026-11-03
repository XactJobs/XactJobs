using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

public class SqlitePeriodicPauseTests : IAsyncLifetime
{
    private const string Daily = "0 0 0 * * *";
    private const string Hourly = "0 0 * * * *";
    private const string JobId = "paused-job";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private IHost? _host;

    public SqlitePeriodicPauseTests()
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
            options.WithWorkerCount(1).WithPollingInterval(1);
        });

        builder.Services.AddScoped<PausingJob>();

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
    public async Task Pause_RemovesThePendingRun()
    {
        await EnsureAsync(Daily);

        Assert.True(await WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == JobId)));

        await SetActiveAsync(false);

        var periodicJob = await WithDbAsync(db => db.PeriodicJobs.AsNoTracking().SingleAsync(p => p.Id == JobId));

        Assert.False(periodicJob.IsActive);
        Assert.False(await WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == JobId)));
    }

    [Fact]
    public async Task Pause_KeepsARunInProgress()
    {
        await EnsureAsync(Daily);

        await WithDbAsync(db => db.Jobs.Where(j => j.PeriodicJobId == JobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeasedUntil, DateTime.UtcNow.AddMinutes(5))));

        await SetActiveAsync(false);

        Assert.True(await WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == JobId)));
    }

    [Theory]
    [InlineData(Daily)]
    [InlineData(Hourly)]
    public async Task EnsureAgain_KeepsAPausedJobPaused_WithoutAPendingRun(string cronExpressionOnRestart)
    {
        var version = (await EnsureAsync(Daily)).Version;

        await SetActiveAsync(false);

        // like the application starting again, with the same or with a changed definition
        var periodicJob = await EnsureAsync(cronExpressionOnRestart);

        Assert.False(periodicJob.IsActive);
        Assert.Equal(cronExpressionOnRestart == Daily, periodicJob.Version == version);
        Assert.False(await WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == JobId)));
    }

    [Fact]
    public async Task Resume_SchedulesOneRunOfTheCurrentVersion()
    {
        await EnsureAsync(Daily);
        await SetActiveAsync(false);

        var periodicJob = await EnsureAsync(Hourly);

        await SetActiveAsync(true);
        await SetActiveAsync(true);

        var pending = await WithDbAsync(db => db.Jobs.AsNoTracking().Where(j => j.PeriodicJobId == JobId).ToListAsync());

        var run = Assert.Single(pending);

        Assert.Equal(periodicJob.Version, run.PeriodicJobVersion);
        Assert.Equal(Hourly, run.CronExpression);
        Assert.True((await WithDbAsync(db => db.PeriodicJobs.AsNoTracking().SingleAsync(p => p.Id == JobId))).IsActive);
    }

    [Fact]
    public async Task ResumingAnActiveJob_DoesNotScheduleAnotherRun()
    {
        await EnsureAsync(Daily);

        await SetActiveAsync(true);

        Assert.Equal(1, await WithDbAsync(db => db.Jobs.CountAsync(j => j.PeriodicJobId == JobId)));
    }

    [Fact]
    public async Task SetActive_OfAMissingJob_ReturnsFalse()
    {
        Assert.False(await WithDbAsync(db => db.JobSetPeriodicActiveAsync("missing", false, CancellationToken.None)));
    }

    [Fact]
    public async Task PendingRunOfAPausedJob_IsSkipped_AndDoesNotScheduleTheNextOne()
    {
        var jobId = await WithDbAsync(async db =>
        {
            db.JobEnsurePeriodic<PausingJob>(x => x.Nothing(), JobId, Daily);

            await db.SaveChangesAsync();

            // paused behind the library's back, so the pending row stays: the scheduled time isn't part of the key on Sqlite
            await db.PeriodicJobs.Where(p => p.Id == JobId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));

            await db.Jobs.Where(j => j.PeriodicJobId == JobId)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.ScheduledAt, DateTime.UtcNow.AddMinutes(-1)));

            return (await db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == JobId)).Id;
        });

        await _host!.StartAsync();

        await WaitForAsync(() => WithDbAsync(db => db.JobHistory.AnyAsync(h => h.Id == jobId)), "the history row");

        Assert.Equal(XactJobStatus.Skipped, (await WithDbAsync(db => db.JobHistory.AsNoTracking().SingleAsync(h => h.Id == jobId))).Status);
        Assert.False(await WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == JobId)));
    }

    [Theory]
    [InlineData(PauseMode.SameContextNotSaved)]
    [InlineData(PauseMode.SameContextSaved)]
    [InlineData(PauseMode.OtherContext)]
    public async Task JobPausedWhileRunning_Completes_AndDoesNotScheduleTheNextOne(PauseMode pauseMode)
    {
        var jobId = await WithDbAsync(async db =>
        {
            db.JobEnsurePeriodic<PausingJob>(x => x.PauseAsync(pauseMode, CancellationToken.None), JobId, Daily);

            await db.SaveChangesAsync();

            await db.Jobs.Where(j => j.PeriodicJobId == JobId)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.ScheduledAt, DateTime.UtcNow.AddMinutes(-1)));

            return (await db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == JobId)).Id;
        });

        await _host!.StartAsync();

        await WaitForAsync(() => WithDbAsync(db => db.JobHistory.AnyAsync(h => h.Id == jobId)), "the history row");

        Assert.Equal(XactJobStatus.Completed, (await WithDbAsync(db => db.JobHistory.AsNoTracking().SingleAsync(h => h.Id == jobId))).Status);
        Assert.False((await WithDbAsync(db => db.PeriodicJobs.AsNoTracking().SingleAsync(p => p.Id == JobId))).IsActive);
        Assert.False(await WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == JobId)));
    }

    public enum PauseMode
    {
        SameContextNotSaved,
        SameContextSaved,
        OtherContext,
    }

    private Task<XactJobPeriodic> EnsureAsync(string cronExpression)
    {
        return WithDbAsync(async db =>
        {
            var periodicJob = await db.JobEnsurePeriodicAsync<PausingJob>(x => x.Nothing(), JobId, cronExpression, CancellationToken.None);

            await db.SaveChangesAsync();

            return periodicJob;
        });
    }

    private Task SetActiveAsync(bool isActive)
    {
        return WithDbAsync(async db =>
        {
            Assert.True(await db.JobSetPeriodicActiveAsync(JobId, isActive, CancellationToken.None));

            return await db.SaveChangesAsync();
        });
    }

    private async Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        using var scope = _host!.Services.CreateScope();

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

    public sealed class PausingJob(TestDbContext db, IServiceScopeFactory scopeFactory)
    {
        public void Nothing()
        {
        }

        public async Task PauseAsync(PauseMode pauseMode, CancellationToken cancellationToken)
        {
            if (pauseMode == PauseMode.OtherContext)
            {
                // like the dashboard pausing it while the job runs
                using var scope = scopeFactory.CreateScope();
                var otherDb = scope.ServiceProvider.GetRequiredService<TestDbContext>();

                await otherDb.JobSetPeriodicActiveAsync(JobId, false, cancellationToken);
                await otherDb.SaveChangesAsync(cancellationToken);

                return;
            }

            await db.JobSetPeriodicActiveAsync(JobId, false, cancellationToken);

            if (pauseMode == PauseMode.SameContextSaved)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
