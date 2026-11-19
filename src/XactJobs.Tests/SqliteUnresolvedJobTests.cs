using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

/// <summary>
/// A job whose type or method this node doesn't have (enqueued by a newer version, during a rolling deployment) is put back for
/// another node during the grace period, and fails as any other attempt after it.
/// </summary>
public class SqliteUnresolvedJobTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private IHost? _host;

    public SqliteUnresolvedJobTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
    }

    public Task InitializeAsync() => _memoryConnection.OpenAsync();

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await _memoryConnection.DisposeAsync();
    }

    [Theory]
    [InlineData("method")]
    [InlineData("type")]
    [InlineData("assembly")]
    public async Task JobNotFoundOnThisNode_IsPutBack_WithoutAnAttempt(string missing)
    {
        await CreateHostAsync();

        var typeName = await GetTypeNameAsync();

        var (jobTypeName, methodName) = missing switch
        {
            "method" => (typeName, "Run(System.Int32)"), // the newer version's overload
            "type" => ("XactJobs.Tests.NewerJob, XactJobs.Tests", "Run(System.String)"),
            _ => ("Newer.Jobs.NewerJob, Newer.Jobs", "Run(System.String)"),
        };

        var scheduledAt = TruncateToMilliseconds(DateTime.UtcNow);

        var id = await AddJobAsync(jobTypeName, methodName, scheduledAt);

        await _host!.StartAsync();

        var job = await WaitForPutBackAsync(id, scheduledAt);

        Assert.Equal(0, job.ErrorCount);
        Assert.Equal(scheduledAt, job.OriginallyScheduledAt);
        Assert.Null(job.LeasedUntil);
        Assert.True(job.ScheduledAt > scheduledAt);

        Assert.False(await WithDbAsync(db => db.JobHistory.AnyAsync(h => h.Id == id)));
        Assert.Empty(UnresolvedTestJob.Calls);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(60, 60)]
    [InlineData(3600, 300)]
    public async Task JobNotFoundOnThisNode_IsMovedByTheTimeItWaited_Between5SecondsAnd5Minutes(int waitedSeconds, int expectedDelaySeconds)
    {
        await CreateHostAsync();

        var typeName = await GetTypeNameAsync();

        var scheduledAt = TruncateToMilliseconds(DateTime.UtcNow.AddSeconds(-waitedSeconds));

        var id = await AddJobAsync(typeName, "Run(System.Int32)", scheduledAt);

        await _host!.StartAsync();

        var job = await WaitForPutBackAsync(id, scheduledAt);

        var delay = job.ScheduledAt - DateTime.UtcNow;

        // the claim and this check take some time too
        Assert.InRange(delay, TimeSpan.FromSeconds(expectedDelaySeconds - 5), TimeSpan.FromSeconds(expectedDelaySeconds + 5));
    }

    [Fact]
    public async Task JobNotFoundOnThisNode_AfterTheGracePeriod_Fails()
    {
        await CreateHostAsync(options => options.WithUnresolvedJobGracePeriodInHours(24));

        var typeName = await GetTypeNameAsync();

        var id = await AddJobAsync(typeName, "Run(System.Int32)", DateTime.UtcNow.AddHours(-25));

        var history = await RunAndGetFirstHistoryAsync(id);

        Assert.Equal(XactJobStatus.Failed, history.Status);
        Assert.Equal(1, history.ErrorCount);
        Assert.Contains("'Run(System.Int32)' wasn't found", history.ErrorMessage);
    }

    [Fact]
    public async Task JobNotFoundOnThisNode_WithoutAGracePeriod_FailsAtOnce()
    {
        await CreateHostAsync(options => options.WithUnresolvedJobGracePeriodInHours(0));

        var id = await AddJobAsync("Newer.Jobs.NewerJob, Newer.Jobs", "Run(System.String)", DateTime.UtcNow);

        var history = await RunAndGetFirstHistoryAsync(id);

        Assert.Equal(XactJobStatus.Failed, history.Status);
        Assert.Contains("Newer.Jobs", history.ErrorMessage);
    }

    [Fact]
    public async Task JobWithoutASignature_IsNotFound()
    {
        // jobs stored before 0.1.7 have only the method's name: the method is found by its signature only
        await CreateHostAsync(options => options.WithUnresolvedJobGracePeriodInHours(0));

        var typeName = await GetTypeNameAsync();

        var id = await AddJobAsync(typeName, nameof(UnresolvedTestJob.Run), DateTime.UtcNow);

        var history = await RunAndGetFirstHistoryAsync(id);

        Assert.Equal(XactJobStatus.Failed, history.Status);
        Assert.Contains("'Run' wasn't found", history.ErrorMessage);
        Assert.Empty(UnresolvedTestJob.Calls);
    }

    [Theory]
    [InlineData(nameof(UnresolvedTestJob.ThrowMissingMethod))]
    [InlineData(nameof(UnresolvedTestJob.ThrowFileNotFound))]
    public async Task JobThatThrowsALookupException_FailsAsAnyOtherAttempt(string method)
    {
        // its own exceptions are never taken for a job this node doesn't have
        await CreateHostAsync();

        var id = await WithDbAsync(async db =>
        {
            var job = method == nameof(UnresolvedTestJob.ThrowMissingMethod)
                ? db.JobEnqueue<UnresolvedTestJob>(x => x.ThrowMissingMethod())
                : db.JobEnqueue<UnresolvedTestJob>(x => x.ThrowFileNotFound());

            await db.SaveChangesAsync();

            return job.Id;
        });

        var history = await RunAndGetFirstHistoryAsync(id);

        Assert.Equal(XactJobStatus.Failed, history.Status);
        Assert.Equal(1, history.ErrorCount);
    }

    private async Task CreateHostAsync(Action<XactJobsOptionsBuilder<TestDbContext>>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString));

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options.WithWorkerCount(1).WithPollingInterval(1);

            configure?.Invoke(options);
        });

        builder.Services.AddScoped<UnresolvedTestJob>();

        _host = builder.Build();

        await WithDbAsync(db => db.Database.EnsureCreatedAsync());
    }

    private Task<string> GetTypeNameAsync()
    {
        return WithDbAsync(db => Task.FromResult(db.JobEnqueue<UnresolvedTestJob>(x => x.Run("template")).TypeName));
    }

    private Task<long> AddJobAsync(string typeName, string methodName, DateTime scheduledAt)
    {
        return WithDbAsync(async db =>
        {
            var job = new XactJob(0, scheduledAt, typeName, methodName, JsonSerializer.Serialize(new object[] { "x" }), QueueNames.Default);

            db.Jobs.Add(job);

            await db.SaveChangesAsync();

            return job.Id;
        });
    }

    /// <summary>The job's row once a worker has put it back: not leased, and moved from its scheduled time.</summary>
    private async Task<XactJob> WaitForPutBackAsync(long id, DateTime scheduledAt)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (true)
        {
            var job = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == id));

            if (job.Leaser == null && job.ScheduledAt != scheduledAt) return job;

            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the job to be put back");

            await Task.Delay(50);
        }
    }

    private async Task<XactJobHistory> RunAndGetFirstHistoryAsync(long id)
    {
        await _host!.StartAsync();

        var deadline = DateTime.UtcNow + WaitTimeout;

        while (true)
        {
            var history = await WithDbAsync(db => db.JobHistory.AsNoTracking().Where(h => h.Id == id).OrderBy(h => h.ProcessedAt).FirstOrDefaultAsync());

            if (history != null) return history;

            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the history row");

            await Task.Delay(50);
        }
    }

    private async Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        using var scope = _host!.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    private static DateTime TruncateToMilliseconds(DateTime value)
    {
        return new DateTime(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, value.Kind);
    }

    public class UnresolvedTestJob
    {
        // no test runs it: its calls would be a job taken for another one
        public static readonly System.Collections.Concurrent.ConcurrentQueue<string> Calls = new();

        public void Run(string value) => Calls.Enqueue(value);

        public void ThrowMissingMethod() => throw new MissingMethodException("the job's own lookup failed");

        public void ThrowFileNotFound() => throw new FileNotFoundException("the job's own file is missing", "missing.txt");
    }
}
