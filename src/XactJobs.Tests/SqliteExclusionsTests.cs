using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

/// <summary>
/// A periodic run whose time the exclusions leave out when it's claimed (they changed after it was scheduled) is skipped.
/// </summary>
public class SqliteExclusionsTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private IHost? _host;

    public SqliteExclusionsTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
    }

    public async Task InitializeAsync()
    {
        await _memoryConnection.OpenAsync();

        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString));
        builder.Services.AddXactJobs<TestDbContext>(options => options.WithWorkerCount(1).WithPollingInterval(1));
        builder.Services.AddScoped<ChangingJobs>();

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
    public async Task RunThatTheExclusionsLeaveOut_IsSkipped_AndTheNextRunIsScheduled()
    {
        var now = DateTime.UtcNow;

        var jobId = await WithDbAsync(async db =>
        {
            db.JobEnsurePeriodic(() => ExcludedJobs.Run(), "excluded", CronExpressions.Daily);

            await db.SaveChangesAsync();

            // due now, and today excluded, as if the exclusions changed by hand while it was pending
            await db.Jobs.ExecuteUpdateAsync(s => s.SetProperty(j => j.ScheduledAt, now.AddMinutes(-1)));
            await db.PeriodicJobs.ExecuteUpdateAsync(s => s.SetProperty(p => p.Exclusions, $"{now.AddMinutes(-1):yyyy-MM-dd}"));

            return (await db.Jobs.AsNoTracking().SingleAsync()).Id;
        });

        await _host!.StartAsync();

        var deadline = DateTime.UtcNow + WaitTimeout;

        while (!await WithDbAsync(db => db.JobHistory.AnyAsync(h => h.Id == jobId)))
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the history row");

            await Task.Delay(50);
        }

        var history = await WithDbAsync(db => db.JobHistory.AsNoTracking().SingleAsync(h => h.Id == jobId));

        Assert.Equal(XactJobStatus.Skipped, history.Status);
        Assert.Contains("exclusions", history.ErrorMessage);
        Assert.Equal(0, ExcludedJobs.Runs);

        var next = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == "excluded"));

        Assert.True(next.ScheduledAt > now);
        Assert.NotEqual(DateOnly.FromDateTime(now.AddMinutes(-1)), DateOnly.FromDateTime(next.ScheduledAt));
    }

    [Fact]
    public async Task RunThatRunsWhileTheExclusionsChange_SchedulesItsNextRunWithTheNewOnes()
    {
        var now = DateTime.UtcNow;
        var tomorrow = DateOnly.FromDateTime(now).AddDays(1);

        var jobId = await WithDbAsync(async db =>
        {
            db.JobEnsurePeriodic<ChangingJobs>(x => x.ExcludeTomorrow(), "changing", CronExpressions.Daily);

            await db.SaveChangesAsync();

            await db.Jobs.ExecuteUpdateAsync(s => s.SetProperty(j => j.ScheduledAt, now.AddMinutes(-1)));

            return (await db.Jobs.AsNoTracking().SingleAsync()).Id;
        });

        await _host!.StartAsync();

        var deadline = DateTime.UtcNow + WaitTimeout;

        while (!await WithDbAsync(db => db.JobHistory.AnyAsync(h => h.Id == jobId)))
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the history row");

            await Task.Delay(50);
        }

        Assert.Equal(XactJobStatus.Completed, (await WithDbAsync(db => db.JobHistory.AsNoTracking().SingleAsync(h => h.Id == jobId))).Status);

        // midnight tomorrow is excluded now: the day after
        var next = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == "changing"));

        Assert.Equal(tomorrow.AddDays(1).ToDateTime(TimeOnly.MinValue), next.ScheduledAt);
        Assert.Equal($"{tomorrow:yyyy-MM-dd}", (await WithDbAsync(db => db.PeriodicJobs.AsNoTracking().SingleAsync(p => p.Id == "changing"))).Exclusions);
    }

    private async Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        using var scope = _host!.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    /// <summary>Changes its periodic job's exclusions while it runs, as another node's ensure would.</summary>
    public class ChangingJobs(TestDbContext db)
    {
        public async Task ExcludeTomorrow()
        {
            var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

            await db.PeriodicJobs.Where(p => p.Id == "changing").ExecuteUpdateAsync(s => s.SetProperty(p => p.Exclusions, $"{tomorrow:yyyy-MM-dd}"));
        }
    }

    public class ExcludedJobs
    {
        public static int Runs;

        public static void Run() => Interlocked.Increment(ref Runs);
    }
}
