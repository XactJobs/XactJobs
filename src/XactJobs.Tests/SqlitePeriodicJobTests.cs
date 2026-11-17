using Microsoft.EntityFrameworkCore;

namespace XactJobs.Tests;

public class SqlitePeriodicJobTests : IDisposable
{
    private readonly TestDbContext _context;

    public SqlitePeriodicJobTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _context = new TestDbContext(options);
        _context.Database.OpenConnection();
        _context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _context.Database.CloseConnection();
        _context.Dispose();
    }

    [Fact]
    public async Task JobEnsurePeriodicAsync_WithNewJob_CreatesPeriodicJob()
    {
        // Arrange
        const string jobId = "test-periodic-job";
        const string cronExpression = "0 0 0 * * *"; // Daily at midnight

        // Act
        var periodicJob = await _context.JobEnsurePeriodicAsync<TestService>(
            x => x.DoWork(),
            jobId,
            cronExpression,
            CancellationToken.None);
        await _context.SaveChangesAsync();

        // Assert
        Assert.NotNull(periodicJob);
        Assert.Equal(jobId, periodicJob.Id);
        Assert.Equal(cronExpression, periodicJob.CronExpression);
        Assert.Equal(QueueNames.Default, periodicJob.Queue);
        Assert.True(periodicJob.IsActive);
        Assert.Contains(nameof(TestService), periodicJob.TypeName);
        Assert.Equal("DoWork()", periodicJob.MethodName);

        var savedPeriodicJob = await _context.PeriodicJobs.FirstAsync();
        Assert.Equal(jobId, savedPeriodicJob.Id);
    }

    [Theory]
    [InlineData("* * * * * *")]
    [InlineData("0 */5 * * * *")]
    [InlineData("0 0 0 * * *")]
    public async Task JobEnsurePeriodicAsync_SchedulesTheRunAtAWholeSecond(string cronExpression)
    {
        // a run's cron slot is its XactJobContext.OriginallyScheduledAt: it must not keep a fraction of the current second
        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "whole-second", cronExpression, CancellationToken.None);
        await _context.SaveChangesAsync();

        var run = await _context.Jobs.SingleAsync();

        Assert.Equal(0, run.ScheduledAt.Ticks % TimeSpan.TicksPerSecond);
        Assert.True(run.ScheduledAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task JobEnsurePeriodicAsync_WithCustomQueue_CreatesPeriodicJobInQueue()
    {
        // Arrange
        const string jobId = "custom-queue-job";
        const string cronExpression = "0 */5 * * * *"; // Every 5 minutes
        const string customQueue = "periodic-queue";

        // Act
        var periodicJob = await _context.JobEnsurePeriodicAsync<TestService>(
            x => x.DoWork(),
            jobId,
            customQueue,
            cronExpression,
            CancellationToken.None);
        await _context.SaveChangesAsync();

        // Assert
        Assert.Equal(customQueue, periodicJob.Queue);
    }

    [Fact]
    public async Task JobEnsurePeriodicAsync_WithAsyncTask_CreatesPeriodicJob()
    {
        // Arrange
        const string jobId = "async-periodic-job";
        const string cronExpression = "0 0 12 * * *"; // Daily at noon

        // Act
        var periodicJob = await _context.JobEnsurePeriodicAsync<TestService>(
            x => x.DoWorkAsync(),
            jobId,
            cronExpression,
            CancellationToken.None);
        await _context.SaveChangesAsync();

        // Assert
        Assert.Equal("DoWorkAsync()", periodicJob.MethodName);
    }

    [Fact]
    public async Task JobEnsurePeriodicAsync_CalledTwiceWithSameId_UpdatesExistingJob()
    {
        // Arrange
        const string jobId = "update-test-job";
        const string cronExpression1 = "0 0 0 * * *";
        const string cronExpression2 = "0 0 12 * * *";

        // Act
        var periodicJob1 = await _context.JobEnsurePeriodicAsync<TestService>(
            x => x.DoWork(),
            jobId,
            cronExpression1,
            CancellationToken.None);
        await _context.SaveChangesAsync();

        var version1 = periodicJob1.Version;

        var periodicJob2 = await _context.JobEnsurePeriodicAsync<TestService>(
            x => x.DoWorkAsync(),
            jobId,
            cronExpression2,
            CancellationToken.None);
        await _context.SaveChangesAsync();

        // Assert
        Assert.Equal(jobId, periodicJob2.Id);
        Assert.Equal(cronExpression2, periodicJob2.CronExpression);
        Assert.Equal("DoWorkAsync()", periodicJob2.MethodName);
        Assert.NotEqual(version1, periodicJob2.Version);

        var count = await _context.PeriodicJobs.CountAsync();
        Assert.Equal(1, count); // Should only have one periodic job
    }

    [Fact]
    public void JobEnsurePeriodic_SynchronousVersion_CreatesPeriodicJob()
    {
        // Arrange
        const string jobId = "sync-periodic-job";
        const string cronExpression = "0 0 6 * * *"; // Daily at 6 AM

        // Act
        var periodicJob = _context.JobEnsurePeriodic<TestService>(
            x => x.DoWork(),
            jobId,
            cronExpression);
        _context.SaveChanges();

        // Assert
        Assert.NotNull(periodicJob);
        Assert.Equal(jobId, periodicJob.Id);
        Assert.Equal(cronExpression, periodicJob.CronExpression);
    }

    [Fact]
    public void JobEnsurePeriodic_WithCustomQueue_CreatesJobInQueue()
    {
        // Arrange
        const string jobId = "sync-queue-job";
        const string cronExpression = "0 0 * * * *"; // Every hour
        const string customQueue = "hourly-queue";

        // Act
        var periodicJob = _context.JobEnsurePeriodic<TestService>(
            x => x.DoWork(),
            jobId,
            customQueue,
            cronExpression);
        _context.SaveChanges();

        // Assert
        Assert.Equal(customQueue, periodicJob.Queue);
    }

    [Fact]
    public async Task JobDeletePeriodicAsync_WithExistingJob_DeletesJob()
    {
        // Arrange
        const string jobId = "job-to-delete";
        const string cronExpression = "0 0 0 * * *";

        await _context.JobEnsurePeriodicAsync<TestService>(
            x => x.DoWork(),
            jobId,
            cronExpression,
            CancellationToken.None);
        await _context.SaveChangesAsync();

        // Act
        var result = await _context.JobDeletePeriodicAsync(jobId, CancellationToken.None);
        await _context.SaveChangesAsync();

        // Assert
        Assert.True(result);
        var count = await _context.PeriodicJobs.CountAsync();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task JobDeletePeriodicAsync_WithNonExistingJob_ReturnsFalse()
    {
        // Act
        var result = await _context.JobDeletePeriodicAsync("non-existing-job", CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task JobEnsurePeriodicAsync_CreatesScheduledJobAutomatically()
    {
        // Arrange
        const string jobId = "job-with-scheduled";
        const string cronExpression = "0 0 0 * * *";

        // Act
        await _context.JobEnsurePeriodicAsync<TestService>(
            x => x.DoWork(),
            jobId,
            cronExpression,
            CancellationToken.None);
        await _context.SaveChangesAsync();

        // Assert
        var jobs = await _context.Jobs.ToListAsync();
        Assert.Single(jobs);
        Assert.Equal(jobId, jobs[0].PeriodicJobId);
        Assert.NotNull(jobs[0].CronExpression);
    }

    [Fact]
    public async Task JobEnsurePeriodic_WithComplexCronExpression_CreatesJob()
    {
        // Arrange
        const string jobId = "complex-cron-job";
        const string cronExpression = "0 0 0,12 1 */2 *"; // Complex expression

        // Act
        var periodicJob = await _context.JobEnsurePeriodicAsync<TestService>(
            x => x.DoWork(),
            jobId,
            cronExpression,
            CancellationToken.None);
        await _context.SaveChangesAsync();

        // Assert
        Assert.Equal(cronExpression, periodicJob.CronExpression);
    }

    [Fact]
    public async Task JobEnsurePeriodic_MultiplePeriodicJobs_CreatesAllJobs()
    {
        // Act
        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "job1", "0 0 0 * * *", CancellationToken.None);
        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "job2", "0 0 12 * * *", CancellationToken.None);
        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "job3", "0 */15 * * * *", CancellationToken.None);
        await _context.SaveChangesAsync();

        // Assert
        var periodicJobCount = await _context.PeriodicJobs.CountAsync();
        var scheduledJobCount = await _context.Jobs.CountAsync();

        Assert.Equal(3, periodicJobCount);
        Assert.Equal(3, scheduledJobCount); // Each periodic job creates a scheduled job
    }

    [Fact]
    public async Task JobEnsurePeriodicAsync_InATimeZone_StoresIt_AndRunsAtTheLocalTime()
    {
        var zagreb = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zagreb");

        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "zagreb", new XactCronSchedule("0 0 9 * * *", "Europe/Zagreb"), CancellationToken.None);
        await _context.SaveChangesAsync();

        _context.ChangeTracker.Clear();

        Assert.Equal("Europe/Zagreb", (await _context.PeriodicJobs.SingleAsync()).TimeZone);

        var run = await _context.Jobs.SingleAsync();
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(run.ScheduledAt, DateTimeKind.Utc), zagreb);

        Assert.Equal((9, 0, 0), (local.Hour, local.Minute, local.Second));
        Assert.True(run.ScheduledAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task JobEnsurePeriodicAsync_WithoutATimeZone_StoresNull()
    {
        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "utc", "0 0 9 * * *", CancellationToken.None);
        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "utc-explicit", new XactCronSchedule("0 0 9 * * *", "UTC"), CancellationToken.None);
        await _context.SaveChangesAsync();

        Assert.All(await _context.PeriodicJobs.ToListAsync(), p => Assert.Null(p.TimeZone));
    }

    [Fact]
    public async Task JobEnsurePeriodicAsync_ChangingTheTimeZone_ChangesTheDefinition()
    {
        var utc = await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "moving", "0 0 9 * * *", CancellationToken.None);
        await _context.SaveChangesAsync();

        var versionInUtc = utc.Version;

        // the same schedule (UTC is no time zone) keeps the version and the run
        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "moving", new XactCronSchedule("0 0 9 * * *", TimeZoneInfo.Utc), CancellationToken.None);
        await _context.SaveChangesAsync();

        Assert.Equal(versionInUtc, (await _context.PeriodicJobs.SingleAsync()).Version);
        Assert.Equal(1, await _context.Jobs.CountAsync());

        // a time zone is a new version, with a run of its own
        var zoned = await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "moving", new XactCronSchedule("0 0 9 * * *", "America/New_York"), CancellationToken.None);
        await _context.SaveChangesAsync();

        Assert.Equal("America/New_York", zoned.TimeZone);
        Assert.NotEqual(versionInUtc, zoned.Version);
        Assert.True(await _context.Jobs.AnyAsync(j => j.PeriodicJobId == "moving" && j.PeriodicJobVersion == zoned.Version));
    }

    [Fact]
    public async Task JobEnsurePeriodicAsync_WithExclusions_StoresThem_AndSchedulesAroundThem()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var exclusions = $"{today:yyyy-MM-dd},{today.AddDays(1):yyyy-MM-dd}";

        var periodic = await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "excluding",
            new XactCronSchedule("0 0 9 * * *") { Exclusions = exclusions }, CancellationToken.None);
        await _context.SaveChangesAsync();

        Assert.Equal($"{today:yyyy-MM-dd}, {today.AddDays(1):yyyy-MM-dd}", periodic.Exclusions);
        Assert.Equal(today.AddDays(2).ToDateTime(new TimeOnly(9, 0)), (await _context.Jobs.SingleAsync()).ScheduledAt);
    }

    [Fact]
    public async Task JobEnsurePeriodicAsync_ChangingTheExclusions_KeepsTheVersion_AndMovesThePendingRun()
    {
        var periodic = await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "moving_run", "0 0 9 * * *", CancellationToken.None);
        await _context.SaveChangesAsync();

        var version = periodic.Version;
        var run = await _context.Jobs.AsNoTracking().SingleAsync();
        var runDate = DateOnly.FromDateTime(run.ScheduledAt);

        // excluding the run's date: the same version, the run moves to the day after
        var schedule = new XactCronSchedule("0 0 9 * * *") { Exclusions = $"{runDate:yyyy-MM-dd}" };

        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "moving_run", schedule, CancellationToken.None);
        await _context.SaveChangesAsync();

        var moved = await _context.Jobs.AsNoTracking().SingleAsync();

        Assert.Equal(version, (await _context.PeriodicJobs.AsNoTracking().SingleAsync()).Version);
        Assert.Equal(version, moved.PeriodicJobVersion);
        Assert.Equal(runDate.AddDays(1).ToDateTime(new TimeOnly(9, 0)), moved.ScheduledAt);

        // the same list, written differently: nothing changes
        _context.ChangeTracker.Clear();

        _context.JobEnsurePeriodic<TestService>(x => x.DoWork(), "moving_run", new XactCronSchedule("0 0 9 * * *") { Exclusions = $" {runDate:yyyy-MM-dd} ," });
        await _context.SaveChangesAsync();

        Assert.Equal(moved.Id, (await _context.Jobs.AsNoTracking().SingleAsync()).Id);
    }

    [Fact]
    public async Task JobEnsurePeriodicAsync_ChangingTheExclusions_LeavesARunningRun()
    {
        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "running", "0 0 9 * * *", CancellationToken.None);
        await _context.SaveChangesAsync();

        // claimed by a worker: it schedules the next run itself, with the new exclusions
        await _context.Jobs.ExecuteUpdateAsync(s => s.SetProperty(j => j.LeasedUntil, DateTime.UtcNow.AddMinutes(5)));

        var run = await _context.Jobs.AsNoTracking().SingleAsync();

        _context.ChangeTracker.Clear();

        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "running",
            new XactCronSchedule("0 0 9 * * *") { Exclusions = $"{run.ScheduledAt:yyyy-MM-dd}" }, CancellationToken.None);
        await _context.SaveChangesAsync();

        Assert.Equal(run.Id, (await _context.Jobs.AsNoTracking().SingleAsync()).Id);
    }

    [Theory]
    [InlineData(-60, 0)] // due (overdue, or moved by Run now): it runs now, or is skipped if the new list excludes it
    [InlineData(60, 1)]  // a retry
    public async Task JobEnsurePeriodicAsync_ChangingTheExclusions_KeepsADueRunOrARetry(int minutes, int errorCount)
    {
        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "kept", "0 0 9 * * *", CancellationToken.None);
        await _context.SaveChangesAsync();

        await _context.Jobs.ExecuteUpdateAsync(s => s
            .SetProperty(j => j.ScheduledAt, DateTime.UtcNow.AddMinutes(minutes))
            .SetProperty(j => j.ErrorCount, errorCount));

        var run = await _context.Jobs.AsNoTracking().SingleAsync();

        _context.ChangeTracker.Clear();

        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "kept",
            new XactCronSchedule("0 0 9 * * *") { Exclusions = $"{run.ScheduledAt:yyyy-MM-dd}" }, CancellationToken.None);
        await _context.SaveChangesAsync();

        Assert.Equal(run.Id, (await _context.Jobs.AsNoTracking().SingleAsync()).Id);
    }

    [Fact]
    public async Task JobEnsurePeriodicAsync_ChangingTheExclusions_KeepsARunWhoseTimeTheyDontChange()
    {
        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "unchanged", "0 0 9 * * *", CancellationToken.None);
        await _context.SaveChangesAsync();

        var run = await _context.Jobs.AsNoTracking().SingleAsync();

        _context.ChangeTracker.Clear();

        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "unchanged",
            new XactCronSchedule("0 0 9 * * *") { Exclusions = $"{run.ScheduledAt.AddDays(10):yyyy-MM-dd}" }, CancellationToken.None);
        await _context.SaveChangesAsync();

        Assert.Equal(run.Id, (await _context.Jobs.AsNoTracking().SingleAsync()).Id);
    }

    [Fact]
    public void Excludes_OnANodeWithoutTheTimeZone_IsFalse()
    {
        var periodic = new XactJobPeriodic("zone", DateTime.UtcNow, DateTime.UtcNow, "0 0 9 * * *", "T", "M()", "[]", QueueNames.Default,
                                           isActive: true, version: 2, timeZone: "Nowhere/Atlantis", exclusions: "00:00-23:59");

        Assert.False(periodic.Excludes(DateTime.UtcNow));
    }

    [Fact]
    public async Task JobEnsurePeriodicAsync_ChangingTheExclusionsOfAPausedJob_SchedulesNothing()
    {
        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "paused", "0 0 9 * * *", CancellationToken.None);
        await _context.SaveChangesAsync();

        await _context.JobSetPeriodicActiveAsync("paused", false, CancellationToken.None);
        await _context.SaveChangesAsync();

        await _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "paused",
            new XactCronSchedule("0 0 9 * * *") { Exclusions = "12-25" }, CancellationToken.None);
        await _context.SaveChangesAsync();

        Assert.Equal("12-25", (await _context.PeriodicJobs.AsNoTracking().SingleAsync()).Exclusions);
        Assert.Equal(0, await _context.Jobs.CountAsync());
    }

    [Fact]
    public async Task JobEnsurePeriodicAsync_RefusesADefaultSchedule()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _context.JobEnsurePeriodicAsync<TestService>(x => x.DoWork(), "default", default(XactCronSchedule), CancellationToken.None));
    }
}
