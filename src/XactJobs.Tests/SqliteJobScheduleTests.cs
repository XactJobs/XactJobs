using Microsoft.EntityFrameworkCore;

namespace XactJobs.Tests;

public class SqliteJobScheduleTests : IDisposable
{
    private readonly TestDbContext _context;

    public SqliteJobScheduleTests()
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
    public void JobScheduleAt_WithSpecificDateTime_CreatesJobWithCorrectScheduledTime()
    {
        // Arrange
        var scheduleTime = DateTime.UtcNow.AddHours(1);

        // Act
        var job = _context.JobScheduleAt<TestService>(x => x.DoWork(), scheduleTime);
        _context.SaveChanges();

        // Assert
        Assert.NotNull(job);
        Assert.Equal(scheduleTime, job.ScheduledAt);

        var savedJob = _context.Jobs.First();
        Assert.Equal(scheduleTime, savedJob.ScheduledAt);
    }

    [Fact]
    public void JobScheduleAt_WithActionAndDateTime_CreatesScheduledJob()
    {
        // Arrange
        var scheduleTime = DateTime.UtcNow.AddMinutes(30);
        var testService = new TestService();

        // Act
        var job = _context.JobScheduleAt(() => testService.DoWork(), scheduleTime);
        _context.SaveChanges();

        // Assert
        Assert.Equal(scheduleTime, job.ScheduledAt);
        Assert.Contains(nameof(TestService), job.TypeName);
        Assert.Equal(nameof(TestService.DoWork), job.MethodName);
    }

    [Fact]
    public void JobScheduleAt_WithAsyncTask_CreatesScheduledJob()
    {
        // Arrange
        var scheduleTime = DateTime.UtcNow.AddDays(1);

        // Act
        var job = _context.JobScheduleAt<TestService>(x => x.DoWorkAsync(), scheduleTime);
        _context.SaveChanges();

        // Assert
        Assert.Equal(scheduleTime, job.ScheduledAt);
        Assert.Equal(nameof(TestService.DoWorkAsync), job.MethodName);
    }

    [Fact]
    public void JobScheduleAt_WithCustomQueue_CreatesJobInSpecifiedQueue()
    {
        // Arrange
        var scheduleTime = DateTime.UtcNow.AddHours(2);
        const string customQueue = "scheduled-queue";

        // Act
        var job = _context.JobScheduleAt<TestService>(x => x.DoWork(), scheduleTime, customQueue);
        _context.SaveChanges();

        // Assert
        Assert.Equal(customQueue, job.Queue);
    }

    [Fact]
    public void JobScheduleIn_WithTimeSpan_CreatesJobScheduledInFuture()
    {
        // Arrange
        var delay = TimeSpan.FromMinutes(15);
        var beforeScheduling = DateTime.UtcNow;

        // Act
        var job = _context.JobScheduleIn<TestService>(x => x.DoWork(), delay);
        _context.SaveChanges();

        // Assert
        Assert.True(job.ScheduledAt >= beforeScheduling.Add(delay));
        Assert.True(job.ScheduledAt <= DateTime.UtcNow.Add(delay).AddSeconds(1));
    }

    [Fact]
    public void JobScheduleIn_WithAction_CreatesScheduledJob()
    {
        // Arrange
        var delay = TimeSpan.FromHours(1);
        var testService = new TestService();

        // Act
        var job = _context.JobScheduleIn(() => testService.DoWork(), delay);
        _context.SaveChanges();

        // Assert
        Assert.NotNull(job);
        Assert.Contains(nameof(TestService), job.TypeName);
    }

    [Fact]
    public void JobScheduleIn_WithAsyncTask_CreatesScheduledJob()
    {
        // Arrange
        var delay = TimeSpan.FromSeconds(30);

        // Act
        var job = _context.JobScheduleIn<TestService>(x => x.DoWorkAsync(), delay);
        _context.SaveChanges();

        // Assert
        Assert.NotNull(job);
        Assert.Equal(nameof(TestService.DoWorkAsync), job.MethodName);
    }

    [Fact]
    public void JobScheduleIn_WithCustomQueue_CreatesJobInSpecifiedQueue()
    {
        // Arrange
        var delay = TimeSpan.FromMinutes(5);
        const string customQueue = "delayed-queue";

        // Act
        var job = _context.JobScheduleIn<TestService>(x => x.DoWork(), delay, customQueue);
        _context.SaveChanges();

        // Assert
        Assert.Equal(customQueue, job.Queue);
    }

    [Fact]
    public void JobSchedule_MultipleJobsWithDifferentTimes_CreatesAllJobsCorrectly()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var time1 = now.AddMinutes(10);
        var time2 = now.AddHours(1);
        var time3 = now.AddDays(1);

        // Act
        _context.JobScheduleAt<TestService>(x => x.DoWork(), time1);
        _context.JobScheduleAt<TestService>(x => x.DoWork(), time2);
        _context.JobScheduleAt<TestService>(x => x.DoWork(), time3);
        _context.SaveChanges();

        // Assert
        var jobs = _context.Jobs.OrderBy(j => j.ScheduledAt).ToList();
        Assert.Equal(3, jobs.Count);
        Assert.Equal(time1, jobs[0].ScheduledAt);
        Assert.Equal(time2, jobs[1].ScheduledAt);
        Assert.Equal(time3, jobs[2].ScheduledAt);
    }

    [Fact]
    public void JobScheduleIn_WithZeroDelay_SchedulesImmediately()
    {
        // Arrange
        var delay = TimeSpan.Zero;

        // Act
        var job = _context.JobScheduleIn<TestService>(x => x.DoWork(), delay);
        _context.SaveChanges();

        // Assert
        Assert.True(job.ScheduledAt <= DateTime.UtcNow);
    }
}
