using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

public class SqlitePeriodicDeleteTests : IAsyncLifetime
{
    private const string Daily = "0 0 0 * * *";
    private const string JobId = "deleted-job";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private IHost? _host;

    public SqlitePeriodicDeleteTests()
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

        builder.Services.AddScoped<SelfDeletingJob>();

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
    public async Task DeleteAndAddAgain_SkipsTheOldPendingRow_AndContinuesWithTheNewOne()
    {
        var (oldJobId, newJobId, version) = await WithDbAsync(async db =>
        {
            db.JobEnsurePeriodic<SelfDeletingJob>(x => x.Nothing(), JobId, Daily);

            await db.SaveChangesAsync();

            var oldJob = await db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == JobId);

            Assert.True(await db.JobDeletePeriodicAsync(JobId, CancellationToken.None));

            await db.SaveChangesAsync();

            var periodicJob = db.JobEnsurePeriodic<SelfDeletingJob>(x => x.Nothing(), JobId, Daily);

            await db.SaveChangesAsync();

            var newJob = await db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == JobId && j.Id != oldJob.Id);

            Assert.NotEqual(oldJob.PeriodicJobVersion, newJob.PeriodicJobVersion);
            Assert.Equal(periodicJob.Version, newJob.PeriodicJobVersion);
            Assert.True(periodicJob.Version > 1, "Version 1 is reserved for rows from before versions existed");

            // make both of them due: the scheduled time isn't part of the key on Sqlite
            await db.Jobs.Where(j => j.PeriodicJobId == JobId)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.ScheduledAt, DateTime.UtcNow.AddMinutes(-1)));

            return (oldJob.Id, newJob.Id, periodicJob.Version);
        });

        await _host!.StartAsync();

        await WaitForAsync(() => WithDbAsync(async db => await db.JobHistory.CountAsync(h => h.PeriodicJobId == JobId) >= 2), "both rows to be processed");

        var history = await WithDbAsync(db => db.JobHistory.AsNoTracking().Where(h => h.PeriodicJobId == JobId).ToDictionaryAsync(h => h.Id));

        Assert.Equal(XactJobStatus.Skipped, history[oldJobId].Status);
        Assert.Equal(XactJobStatus.Completed, history[newJobId].Status);

        // only the new chain continues
        var pending = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == JobId));

        Assert.Equal(version, pending.PeriodicJobVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PeriodicJobDeletingItself_CompletesWithHistory_AndDoesNotRunAgain(bool saveInsideTheJob)
    {
        var jobId = await WithDbAsync(async db =>
        {
            db.JobEnsurePeriodic<SelfDeletingJob>(x => x.DeleteItselfAsync(saveInsideTheJob, CancellationToken.None), JobId, Daily);

            await db.SaveChangesAsync();

            var pending = await db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == JobId);

            // make it due: the scheduled time isn't part of the key on Sqlite
            await db.Jobs.Where(j => j.Id == pending.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.ScheduledAt, DateTime.UtcNow.AddMinutes(-1)));

            return pending.Id;
        });

        await _host!.StartAsync();

        await WaitForAsync(() => WithDbAsync(db => db.JobHistory.AnyAsync(h => h.Id == jobId)), "the history row");

        var history = await WithDbAsync(db => db.JobHistory.AsNoTracking().SingleAsync(h => h.Id == jobId));

        Assert.Equal(XactJobStatus.Completed, history.Status);

        Assert.False(await WithDbAsync(db => db.PeriodicJobs.AnyAsync(p => p.Id == JobId)));
        Assert.False(await WithDbAsync(db => db.Jobs.AnyAsync(j => j.PeriodicJobId == JobId)));
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

    public sealed class SelfDeletingJob(TestDbContext db)
    {
        public void Nothing()
        {
        }

        public async Task DeleteItselfAsync(bool saveInsideTheJob, CancellationToken cancellationToken)
        {
            await db.JobDeletePeriodicAsync(JobId, cancellationToken);

            if (saveInsideTheJob)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
