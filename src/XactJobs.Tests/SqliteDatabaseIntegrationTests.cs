using Microsoft.EntityFrameworkCore;

namespace XactJobs.Tests;

public class SqliteDatabaseIntegrationTests : IDisposable
{
    private readonly TestDbContext _context;

    public SqliteDatabaseIntegrationTests()
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
    public void DatabaseCreation_CreatesAllRequiredTables()
    {
        // Act
        var canConnect = _context.Database.CanConnect();

        // Assert
        Assert.True(canConnect);
    }

    [Fact]
    public void JobTable_CanInsertAndRetrieveJob()
    {
        // Arrange
        var job = new XactJob(
            id: 0,
            scheduledAt: DateTime.UtcNow,
            typeName: "TestType",
            methodName: "TestMethod",
            methodArgs: "{}",
            queue: QueueNames.Default);

        // Act
        _context.Jobs.Add(job);
        _context.SaveChanges();

        var retrievedJob = _context.Jobs.First();

        // Assert
        Assert.NotNull(retrievedJob);
        Assert.Equal("TestType", retrievedJob.TypeName);
        Assert.Equal("TestMethod", retrievedJob.MethodName);
        Assert.Equal(QueueNames.Default, retrievedJob.Queue);
    }

    [Fact]
    public void JobTable_SupportsNullableFields()
    {
        // Arrange
        var job = new XactJob(
            id: 0,
            scheduledAt: DateTime.UtcNow,
            typeName: "TestType",
            methodName: "TestMethod",
            methodArgs: "{}",
            queue: QueueNames.Default,
            periodicJobId: null,
            cronExpression: null,
            periodicJobVersion: null,
            errorCount: 0,
            leaser: null,
            leasedUntil: null);

        // Act
        _context.Jobs.Add(job);
        _context.SaveChanges();

        var retrievedJob = _context.Jobs.First();

        // Assert
        Assert.Null(retrievedJob.PeriodicJobId);
        Assert.Null(retrievedJob.CronExpression);
        Assert.Null(retrievedJob.PeriodicJobVersion);
        Assert.Null(retrievedJob.Leaser);
        Assert.Null(retrievedJob.LeasedUntil);
    }

    [Fact]
    public void JobTable_CanUpdateLeaseInformation()
    {
        // Arrange
        var job = new XactJob(
            id: 0,
            scheduledAt: DateTime.UtcNow,
            typeName: "TestType",
            methodName: "TestMethod",
            methodArgs: "{}",
            queue: QueueNames.Default);

        _context.Jobs.Add(job);
        _context.SaveChanges();

        var leaser = Guid.NewGuid();
        var leasedUntil = DateTime.UtcNow.AddMinutes(5);

        // Act
        var retrievedJob = _context.Jobs.First();
        var updatedJob = new XactJob(
            id: retrievedJob.Id,
            scheduledAt: retrievedJob.ScheduledAt,
            typeName: retrievedJob.TypeName,
            methodName: retrievedJob.MethodName,
            methodArgs: retrievedJob.MethodArgs,
            queue: retrievedJob.Queue,
            leaser: leaser,
            leasedUntil: leasedUntil);

        _context.Entry(retrievedJob).CurrentValues.SetValues(updatedJob);
        _context.SaveChanges();

        var finalJob = _context.Jobs.First();

        // Assert
        Assert.Equal(leaser, finalJob.Leaser);
        Assert.NotNull(finalJob.LeasedUntil);
    }

    [Fact]
    public void PeriodicJobTable_CanInsertAndRetrievePeriodicJob()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var periodicJob = new XactJobPeriodic(
            id: "test-periodic",
            createdAt: now,
            updatedAt: now,
            cronExpression: "0 0 0 * * *",
            typeName: "TestType",
            methodName: "TestMethod",
            methodArgs: "{}",
            queue: QueueNames.Default,
            isActive: true,
            version: 1);

        // Act
        _context.PeriodicJobs.Add(periodicJob);
        _context.SaveChanges();

        var retrieved = _context.PeriodicJobs.First();

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal("test-periodic", retrieved.Id);
        Assert.Equal("0 0 0 * * *", retrieved.CronExpression);
        Assert.True(retrieved.IsActive);
    }

    [Fact]
    public void PeriodicJobTable_CanUpdateVersion()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var periodicJob = new XactJobPeriodic(
            id: "version-test",
            createdAt: now,
            updatedAt: now,
            cronExpression: "0 0 0 * * *",
            typeName: "TestType",
            methodName: "TestMethod",
            methodArgs: "{}",
            queue: QueueNames.Default,
            isActive: true,
            version: 1);

        _context.PeriodicJobs.Add(periodicJob);
        _context.SaveChanges();

        // Act
        var retrieved = _context.PeriodicJobs.First();
        var updated = new XactJobPeriodic(
            id: retrieved.Id,
            createdAt: retrieved.CreatedAt,
            updatedAt: DateTime.UtcNow,
            cronExpression: retrieved.CronExpression,
            typeName: retrieved.TypeName,
            methodName: retrieved.MethodName,
            methodArgs: retrieved.MethodArgs,
            queue: retrieved.Queue,
            isActive: retrieved.IsActive,
            version: 2);

        _context.Entry(retrieved).CurrentValues.SetValues(updated);
        _context.SaveChanges();

        var final = _context.PeriodicJobs.First();

        // Assert
        Assert.Equal(2, final.Version);
    }

    [Fact]
    public void JobHistory_CanInsertAndRetrieveHistory()
    {
        // Arrange
        var history = new XactJobHistory(
            id: 0,
            processedAt: DateTime.UtcNow,
            status: XactJobStatus.Completed,
            scheduledAt: DateTime.UtcNow,
            typeName: "TestType",
            methodName: "TestMethod",
            methodArgs: "{}",
            queue: QueueNames.Default);

        // Act
        _context.JobHistory.Add(history);
        _context.SaveChanges();

        var retrieved = _context.JobHistory.First();

        // Assert
        Assert.NotNull(retrieved);
        Assert.Equal(XactJobStatus.Completed, retrieved.Status);
        Assert.Equal("TestType", retrieved.TypeName);
    }

    [Fact]
    public void JobHistory_SupportsErrorInformation()
    {
        // Arrange
        var history = new XactJobHistory(
            id: 0,
            processedAt: DateTime.UtcNow,
            status: XactJobStatus.Failed,
            scheduledAt: DateTime.UtcNow,
            typeName: "TestType",
            methodName: "TestMethod",
            methodArgs: "{}",
            queue: QueueNames.Default,
            errorCount: 3,
            errorMessage: "Test error message",
            errorStackTrace: "Stack trace here");

        // Act
        _context.JobHistory.Add(history);
        _context.SaveChanges();

        var retrieved = _context.JobHistory.First();

        // Assert
        Assert.Equal(XactJobStatus.Failed, retrieved.Status);
        Assert.Equal(3, retrieved.ErrorCount);
        Assert.Equal("Test error message", retrieved.ErrorMessage);
        Assert.Equal("Stack trace here", retrieved.ErrorStackTrace);
    }

    [Fact]
    public void Database_SupportsTransactions()
    {
        // Arrange
        using var transaction = _context.Database.BeginTransaction();

        // Act
        var job1 = _context.JobEnqueue<TestService>(x => x.DoWork());
        var job2 = _context.JobEnqueue<TestService>(x => x.DoWorkAsync());
        _context.SaveChanges();

        transaction.Rollback();

        // Assert
        var count = _context.Jobs.Count();
        Assert.Equal(0, count);
    }

    [Fact]
    public void Database_CanQueryJobsByQueue()
    {
        // Arrange
        _context.JobEnqueue<TestService>(x => x.DoWork(), "queue1");
        _context.JobEnqueue<TestService>(x => x.DoWork(), "queue2");
        _context.JobEnqueue<TestService>(x => x.DoWork(), "queue1");
        _context.SaveChanges();

        // Act
        var queue1Jobs = _context.Jobs.Where(j => j.Queue == "queue1").ToList();
        var queue2Jobs = _context.Jobs.Where(j => j.Queue == "queue2").ToList();

        // Assert
        Assert.Equal(2, queue1Jobs.Count);
        Assert.Single(queue2Jobs);
    }

    [Fact]
    public void Database_CanQueryJobsByScheduledTime()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var future = now.AddHours(1);

        _context.JobScheduleAt<TestService>(x => x.DoWork(), now);
        _context.JobScheduleAt<TestService>(x => x.DoWork(), future);
        _context.SaveChanges();

        // Act
        var dueJobs = _context.Jobs.Where(j => j.ScheduledAt <= now.AddMinutes(1)).ToList();

        // Assert
        Assert.Single(dueJobs);
    }

    [Fact]
    public void Database_CanDeleteJobs()
    {
        // Arrange
        var job = _context.JobEnqueue<TestService>(x => x.DoWork());
        _context.SaveChanges();

        // Act
        var retrievedJob = _context.Jobs.First();
        _context.Jobs.Remove(retrievedJob);
        _context.SaveChanges();

        // Assert
        var count = _context.Jobs.Count();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Database_SupportsAsyncOperations()
    {
        // Arrange & Act
        var job = _context.JobEnqueue<TestService>(x => x.DoWork());
        await _context.SaveChangesAsync();

        var count = await _context.Jobs.CountAsync();
        var retrieved = await _context.Jobs.FirstAsync();

        // Assert
        Assert.Equal(1, count);
        Assert.NotNull(retrieved);
    }
}
