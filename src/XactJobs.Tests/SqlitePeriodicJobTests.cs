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
        Assert.Equal(nameof(TestService.DoWork), periodicJob.MethodName);

        var savedPeriodicJob = await _context.PeriodicJobs.FirstAsync();
        Assert.Equal(jobId, savedPeriodicJob.Id);
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
        Assert.Equal(nameof(TestService.DoWorkAsync), periodicJob.MethodName);
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
        Assert.Equal(nameof(TestService.DoWorkAsync), periodicJob2.MethodName);
        Assert.True(periodicJob2.Version > version1);

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
}
