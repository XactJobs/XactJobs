using Microsoft.EntityFrameworkCore;

namespace XactJobs.Tests;

public class SqliteJobEnqueueTests : IDisposable
{
    private readonly TestDbContext _context;

    public SqliteJobEnqueueTests()
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
    public void JobEnqueue_WithAction_CreatesJobInDatabase()
    {
        // Arrange
        var testService = new TestService();

        // Act
        var job = _context.JobEnqueue(() => testService.DoWork());
        _context.SaveChanges();

        // Assert
        Assert.NotNull(job);
        Assert.Contains(nameof(TestService), job.TypeName);
        Assert.Equal(nameof(TestService.DoWork), job.MethodName);
        Assert.Equal(QueueNames.Default, job.Queue);
        Assert.True(job.ScheduledAt <= DateTime.UtcNow);

        var savedJob = _context.Jobs.First();
        Assert.True(savedJob.Id > 0);
    }

    [Fact]
    public void JobEnqueue_WithGenericAction_CreatesJobInDatabase()
    {
        // Act
        var job = _context.JobEnqueue<TestService>(x => x.DoWork());
        _context.SaveChanges();

        // Assert
        Assert.NotNull(job);
        Assert.Contains(nameof(TestService), job.TypeName);
        Assert.Equal(nameof(TestService.DoWork), job.MethodName);
        Assert.Equal(QueueNames.Default, job.Queue);

        var savedJob = _context.Jobs.First();
        Assert.True(savedJob.Id > 0);
    }

    [Fact]
    public void JobEnqueue_WithAsyncTask_CreatesJobInDatabase()
    {
        // Arrange
        var testService = new TestService();

        // Act
        var job = _context.JobEnqueue(() => testService.DoWorkAsync());
        _context.SaveChanges();

        // Assert
        Assert.NotNull(job);
        Assert.Contains(nameof(TestService), job.TypeName);
        Assert.Equal(nameof(TestService.DoWorkAsync), job.MethodName);

        var savedJob = _context.Jobs.First();
        Assert.True(savedJob.Id > 0);
    }

    [Fact]
    public void JobEnqueue_WithGenericAsyncTask_CreatesJobInDatabase()
    {
        // Act
        var job = _context.JobEnqueue<TestService>(x => x.DoWorkAsync());
        _context.SaveChanges();

        // Assert
        Assert.NotNull(job);
        Assert.Contains(nameof(TestService), job.TypeName);
        Assert.Equal(nameof(TestService.DoWorkAsync), job.MethodName);

        var savedJob = _context.Jobs.First();
        Assert.True(savedJob.Id > 0);
    }

    [Fact]
    public void JobEnqueue_WithCustomQueue_CreatesJobWithSpecifiedQueue()
    {
        // Arrange
        const string customQueue = "custom-queue";

        // Act
        var job = _context.JobEnqueue<TestService>(x => x.DoWork(), customQueue);
        _context.SaveChanges();

        // Assert
        Assert.Equal(customQueue, job.Queue);

        var savedJob = _context.Jobs.First();
        Assert.Equal(customQueue, savedJob.Queue);
    }

    [Fact]
    public void JobEnqueue_WithParameters_SerializesParametersCorrectly()
    {
        // Arrange
        const string testMessage = "Hello, World!";
        const int testNumber = 42;

        // Act
        var job = _context.JobEnqueue<TestService>(x => x.DoWorkWithParameters(testMessage, testNumber));
        _context.SaveChanges();

        // Assert
        Assert.NotNull(job.MethodArgs);
        Assert.Contains(testMessage, job.MethodArgs);
        Assert.Contains(testNumber.ToString(), job.MethodArgs);
    }

    [Fact]
    public void JobEnqueue_MultipleJobs_CreatesAllJobsInDatabase()
    {
        // Act
        _context.JobEnqueue<TestService>(x => x.DoWork());
        _context.JobEnqueue<TestService>(x => x.DoWorkAsync());
        _context.JobEnqueue<TestService>(x => x.DoWorkWithParameters("test", 1));
        _context.SaveChanges();

        // Assert
        var jobCount = _context.Jobs.Count();
        Assert.Equal(3, jobCount);
    }
}

public class TestService
{
    public void DoWork()
    {
    }

    public Task DoWorkAsync()
    {
        return Task.CompletedTask;
    }

    public void DoWorkWithParameters(string message, int number)
    {
    }
}
