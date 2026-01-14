using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace XactJobs.Tests;

public class SqliteJobExecutionTests : IAsyncLifetime
{
    private IHost? _host;

    // this connecting keeps the memory DB alive during tests
    private SqliteConnection? _memoryConnection;

    public async Task InitializeAsync()
    {
        var cnString = $"Data Source=SqliteTests;Mode=Memory;Cache=Shared;";

        _memoryConnection = new SqliteConnection(cnString);
        await _memoryConnection.OpenAsync();

        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddDbContext<TestDbContext>(options =>
        {
            options.UseSqlite(cnString);
        });

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options.WithPollingInterval(1); // Fast polling for tests (1 second)
        });

        builder.Services.AddSingleton<ExecutableTestService>();
        builder.Services.AddScoped<TestJobWorker>();

        _host = builder.Build();

        // Create the database
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        // Start the background services
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        if (_memoryConnection != null)
        {
            await _memoryConnection.CloseAsync();
            _memoryConnection.Dispose();
        }
    }

    [Fact]
    public async Task Job_WithSyncMethod_ExecutesSuccessfully()
    {
        // Arrange
        var testService = _host!.Services.GetRequiredService<ExecutableTestService>();

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // Act
        db.JobEnqueue<TestJobWorker>(x => x.IncrementCounter());
        await db.SaveChangesAsync();

        // Wait for job to execute (with timeout)
        var executed = await WaitForConditionAsync(() => testService.Counter > 0, TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(executed, "Job did not execute within timeout");
        Assert.Equal(1, testService.Counter);
    }

    [Fact]
    public async Task Job_WithAsyncMethod_ExecutesSuccessfully()
    {
        // Arrange
        var testService = _host!.Services.GetRequiredService<ExecutableTestService>();

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // Act
        db.JobEnqueue<TestJobWorker>(x => x.IncrementCounterAsync());
        await db.SaveChangesAsync();

        // Wait for job to execute
        var executed = await WaitForConditionAsync(() => testService.Counter > 0, TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(executed, "Job did not execute within timeout");
        Assert.Equal(1, testService.Counter);
    }

    [Fact]
    public async Task Job_WithParameters_ExecutesWithCorrectValues()
    {
        // Arrange
        var testService = _host!.Services.GetRequiredService<ExecutableTestService>();
        const string expectedMessage = "Hello from test";
        const int expectedNumber = 42;

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // Act
        db.JobEnqueue<TestJobWorker>(x => x.SetValues(expectedMessage, expectedNumber));
        await db.SaveChangesAsync();

        // Wait for job to execute
        var executed = await WaitForConditionAsync(() => testService.LastMessage != null, TimeSpan.FromSeconds(5));

        // Assert
        Assert.True(executed, "Job did not execute within timeout");
        Assert.Equal(expectedMessage, testService.LastMessage);
        Assert.Equal(expectedNumber, testService.LastNumber);
    }

    [Fact]
    public async Task Job_AfterExecution_MovesToHistory()
    {
        // Arrange
        var testService = _host!.Services.GetRequiredService<ExecutableTestService>();

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // Act
        var job = db.JobEnqueue<TestJobWorker>(x => x.IncrementCounter());
        await db.SaveChangesAsync();
        var jobId = job.Id;

        // Wait for job to execute
        await WaitForConditionAsync(() => testService.Counter > 0, TimeSpan.FromSeconds(5));

        // Give it a moment to move to history
        await Task.Delay(500);

        // Assert
        using var verifyScope = _host.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TestDbContext>();

        var jobStillExists = await verifyDb.Jobs.AnyAsync(j => j.Id == jobId);
        var historyExists = await verifyDb.JobHistory.AnyAsync(h => h.Id == jobId);

        Assert.False(jobStillExists, "Job should be removed from jobs table");
        Assert.True(historyExists, "Job should exist in history table");

        var history = await verifyDb.JobHistory.FirstAsync(h => h.Id == jobId);
        Assert.Equal(XactJobStatus.Completed, history.Status);
    }

    [Fact]
    public async Task Job_ThatThrowsException_MovesToHistoryAsFailed()
    {
        // Arrange
        using var scope = _host!.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // Act
        var job = db.JobEnqueue<TestJobWorker>(x => x.ThrowException());
        await db.SaveChangesAsync();
        var jobId = job.Id;

        // Wait for job to fail (it will retry, so wait a bit)
        await Task.Delay(2000);

        // Assert
        using var verifyScope = _host!.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<TestDbContext>();

        var historyExists = await verifyDb.JobHistory.AnyAsync(h => h.Id == jobId);
        Assert.True(historyExists, "Failed job should be in history");

        var history = await verifyDb.JobHistory.FirstAsync(h => h.Id == jobId);
        Assert.Equal(XactJobStatus.Failed, history.Status);
        Assert.NotNull(history.ErrorMessage);
        Assert.Contains("Test exception", history.ErrorMessage);
    }

    [Fact]
    public async Task MultipleJobs_ExecuteInOrder()
    {
        // Arrange
        var testService = _host!.Services.GetRequiredService<ExecutableTestService>();

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // Act - enqueue 5 jobs
        for (int i = 0; i < 5; i++)
        {
            db.JobEnqueue<TestJobWorker>(x => x.IncrementCounter());
        }
        await db.SaveChangesAsync();

        // Wait for all jobs to execute
        var executed = await WaitForConditionAsync(() => testService.Counter >= 5, TimeSpan.FromSeconds(10));

        // Assert
        Assert.True(executed, "Not all jobs executed within timeout");
        Assert.Equal(5, testService.Counter);
    }

    [Fact]
    public async Task ScheduledJob_ExecutesAtCorrectTime()
    {
        // Arrange
        var testService = _host!.Services.GetRequiredService<ExecutableTestService>();

        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        // Act - schedule job 2 seconds in the future
        var scheduleTime = DateTime.UtcNow.AddSeconds(2);
        db.JobScheduleAt<TestJobWorker>(x => x.IncrementCounter(), scheduleTime);
        await db.SaveChangesAsync();

        // Wait 1 second - job should NOT have executed yet
        await Task.Delay(1000);
        Assert.Equal(0, testService.Counter);

        // Wait another 2 seconds - job should have executed
        var executed = await WaitForConditionAsync(() => testService.Counter > 0, TimeSpan.FromSeconds(3));
        Assert.True(executed, "Scheduled job did not execute");
        Assert.Equal(1, testService.Counter);
    }

    private static async Task<bool> WaitForConditionAsync(Func<bool> condition, TimeSpan timeout)
    {
        var startTime = DateTime.UtcNow;
        while (DateTime.UtcNow - startTime < timeout)
        {
            if (condition())
            {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }
}

public class ExecutableTestService
{
    private int _counter;
    private string? _lastMessage;
    private int _lastNumber;

    public int Counter => _counter;
    public string? LastMessage => _lastMessage;
    public int LastNumber => _lastNumber;

    public void IncrementCounter()
    {
        Interlocked.Increment(ref _counter);
    }

    public Task IncrementCounterAsync()
    {
        Interlocked.Increment(ref _counter);
        return Task.CompletedTask;
    }

    public void SetValues(string message, int number)
    {
        _lastMessage = message;
        _lastNumber = number;
    }

    public void ThrowException()
    {
        throw new InvalidOperationException("Test exception");
    }
}

public class TestJobWorker
{
    private readonly ExecutableTestService _testService;

    public TestJobWorker(ExecutableTestService testService)
    {
        _testService = testService;
    }

    public void IncrementCounter()
    {
        _testService.IncrementCounter();
    }

    public Task IncrementCounterAsync()
    {
        return _testService.IncrementCounterAsync();
    }

    public void SetValues(string message, int number)
    {
        _testService.SetValues(message, number);
    }

    public void ThrowException()
    {
        _testService.ThrowException();
    }
}
