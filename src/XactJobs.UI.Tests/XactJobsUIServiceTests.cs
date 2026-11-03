using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.UI.Tests;

public class XactJobsUIServiceTests : IDisposable
{
    private const string Daily = "0 0 0 * * *";
    private const string PeriodicId = "daily";

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public XactJobsUIServiceTests()
    {
        _connection.Open();

        using var db = CreateDb();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    [Fact]
    public async Task Cancel_MovesThePendingJobToTheHistoryAsCancelled()
    {
        var jobId = await AddJobAsync(DateTime.UtcNow.AddHours(1), errorCount: 2);

        var result = await WithServiceAsync(s => s.CancelJobAsync(jobId, "alice"));

        Assert.Equal(ActionStatus.Done, result.Status);

        await using var db = CreateDb();

        Assert.False(await db.Jobs.AnyAsync());

        var history = await db.JobHistory.SingleAsync();

        Assert.Equal(jobId, history.Id);
        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.Equal(2, history.ErrorCount);
        Assert.Equal($"{XactJobsUIService<UiTestDbContext>.CancelledMessage} by alice", history.ErrorMessage);
        Assert.Null(history.ErrorStackTrace);

        var cancelled = await WithServiceAsync(s => s.GetHistoryAsync(HistoryCategory.Cancelled, 1, 20));
        var failed = await WithServiceAsync(s => s.GetHistoryAsync(HistoryCategory.Failed, 1, 20));

        Assert.Equal(jobId, Assert.Single(cancelled.Items).Id);
        Assert.Empty(failed.Items);
    }

    [Fact]
    public async Task Cancel_OfARunningJob_IsAConflict()
    {
        var jobId = await AddJobAsync(DateTime.UtcNow.AddMinutes(-1), leasedUntil: DateTime.UtcNow.AddMinutes(5));

        var result = await WithServiceAsync(s => s.CancelJobAsync(jobId, null));

        Assert.Equal(ActionStatus.Conflict, result.Status);
        Assert.Equal(1, await CountJobsAsync());
        Assert.Equal(0, await CountHistoryAsync());
    }

    [Fact]
    public async Task Cancel_OfAPeriodicRun_IsAConflict()
    {
        await EnsurePeriodicAsync();

        var run = await GetPeriodicRunAsync();

        var result = await WithServiceAsync(s => s.CancelJobAsync(run.Id, null));

        Assert.Equal(ActionStatus.Conflict, result.Status);
        Assert.Contains("Pause", result.Message);
        Assert.Equal(1, await CountJobsAsync());
    }

    [Fact]
    public async Task Cancel_OfAMissingJob_IsNotFound()
    {
        Assert.Equal(ActionStatus.NotFound, (await WithServiceAsync(s => s.CancelJobAsync(42, null))).Status);
    }

    [Fact]
    public async Task RunNow_MakesAFutureJobDue_KeepingItsFailedAttempts()
    {
        var jobId = await AddJobAsync(DateTime.UtcNow.AddHours(1), errorCount: 1);

        var result = await WithServiceAsync(s => s.RunJobNowAsync(jobId));

        Assert.Equal(ActionStatus.Done, result.Status);

        await using var db = CreateDb();

        var job = await db.Jobs.SingleAsync();

        Assert.True(job.ScheduledAt <= DateTime.UtcNow);
        Assert.Equal(1, job.ErrorCount);
        Assert.Equal(0, await db.JobHistory.CountAsync());
    }

    [Fact]
    public async Task RunNow_OfARunningJob_IsAConflict()
    {
        var scheduledAt = DateTime.UtcNow.AddHours(1);
        var jobId = await AddJobAsync(scheduledAt, leasedUntil: DateTime.UtcNow.AddMinutes(5));

        var result = await WithServiceAsync(s => s.RunJobNowAsync(jobId));

        Assert.Equal(ActionStatus.Conflict, result.Status);

        await using var db = CreateDb();
        Assert.Equal(jobId, (await db.Jobs.SingleAsync()).Id);
    }

    [Fact]
    public async Task Trigger_MovesThePendingRunToNow_SoThePeriodicJobKeepsOneRun()
    {
        await EnsurePeriodicAsync();

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.TriggerPeriodicJobAsync(PeriodicId))).Status);
        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.TriggerPeriodicJobAsync(PeriodicId))).Status);

        var run = await GetPeriodicRunAsync();

        Assert.True(run.ScheduledAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task Trigger_WithoutAPendingRun_SchedulesOneNow()
    {
        await EnsurePeriodicAsync();

        await using (var db = CreateDb())
        {
            await db.Jobs.ExecuteDeleteAsync();
        }

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.TriggerPeriodicJobAsync(PeriodicId))).Status);

        var run = await GetPeriodicRunAsync();

        Assert.True(run.ScheduledAt <= DateTime.UtcNow);
    }

    [Fact]
    public async Task Trigger_OfARunningJob_IsAConflict()
    {
        await EnsurePeriodicAsync();

        await using (var db = CreateDb())
        {
            await db.Jobs.ExecuteUpdateAsync(s => s.SetProperty(j => j.LeasedUntil, DateTime.UtcNow.AddMinutes(5)));
        }

        var result = await WithServiceAsync(s => s.TriggerPeriodicJobAsync(PeriodicId));

        Assert.Equal(ActionStatus.Conflict, result.Status);
        Assert.Equal(1, await CountJobsAsync());
    }

    [Fact]
    public async Task Trigger_OfAPausedJob_IsAConflict()
    {
        await EnsurePeriodicAsync();

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.SetPeriodicJobActiveAsync(PeriodicId, false))).Status);

        var result = await WithServiceAsync(s => s.TriggerPeriodicJobAsync(PeriodicId));

        Assert.Equal(ActionStatus.Conflict, result.Status);
        Assert.Equal(0, await CountJobsAsync());
    }

    [Fact]
    public async Task PauseAndResume_AreShownInTheList()
    {
        await EnsurePeriodicAsync();

        await WithServiceAsync(s => s.SetPeriodicJobActiveAsync(PeriodicId, false));

        var paused = Assert.Single((await WithServiceAsync(s => s.GetPeriodicJobsAsync(1, 20))).Items);

        Assert.False(paused.IsActive);
        Assert.Null(paused.NextRunAt);

        await WithServiceAsync(s => s.SetPeriodicJobActiveAsync(PeriodicId, true));

        var resumed = Assert.Single((await WithServiceAsync(s => s.GetPeriodicJobsAsync(1, 20))).Items);

        Assert.True(resumed.IsActive);
        Assert.NotNull(resumed.NextRunAt);
        Assert.False(resumed.IsRunning);
    }

    [Fact]
    public async Task DeletePeriodic_DeletesIt()
    {
        await EnsurePeriodicAsync();

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.DeletePeriodicJobAsync(PeriodicId))).Status);
        Assert.Equal(ActionStatus.NotFound, (await WithServiceAsync(s => s.DeletePeriodicJobAsync(PeriodicId))).Status);

        await using var db = CreateDb();
        Assert.False(await db.PeriodicJobs.AnyAsync());
    }

    [Fact]
    public async Task Retry_OfAFailedPeriodicRun_EnqueuesAOneOffJob()
    {
        await AddHistoryAsync(7, XactJobStatus.Cancelled, stackTrace: "at Somewhere()", periodicJobId: PeriodicId);

        var result = await WithServiceAsync(s => s.RetryJobAsync(7));

        Assert.Equal(ActionStatus.Done, result.Status);

        await using var db = CreateDb();

        var job = await db.Jobs.SingleAsync();

        Assert.Null(job.PeriodicJobId);
        Assert.Null(job.PeriodicJobVersion);
        Assert.Equal(0, job.ErrorCount);
        Assert.True(job.ScheduledAt <= DateTime.UtcNow);
    }

    [Theory]
    [InlineData(XactJobStatus.Failed)]
    [InlineData(XactJobStatus.Completed)]
    [InlineData(XactJobStatus.Skipped)]
    public async Task Retry_OfAJobThatIsNotFailedOrCancelled_IsAConflict(XactJobStatus status)
    {
        // a failed attempt was already rescheduled by the runner, retrying it would run the job twice
        await AddHistoryAsync(7, status, stackTrace: "at Somewhere()");

        Assert.Equal(ActionStatus.Conflict, (await WithServiceAsync(s => s.RetryJobAsync(7))).Status);
        Assert.Equal(0, await CountJobsAsync());
    }

    [Fact]
    public async Task History_GroupsTheStatuses()
    {
        await AddHistoryAsync(1, XactJobStatus.Completed);
        await AddHistoryAsync(2, XactJobStatus.Cancelled, stackTrace: "at Somewhere()");
        await AddHistoryAsync(3, XactJobStatus.Cancelled);
        await AddHistoryAsync(4, XactJobStatus.Failed, stackTrace: "at Somewhere()");
        await AddHistoryAsync(5, XactJobStatus.Skipped);

        async Task<long> SingleIdAsync(HistoryCategory category) =>
            Assert.Single((await WithServiceAsync(s => s.GetHistoryAsync(category, 1, 20))).Items).Id;

        Assert.Equal(1, await SingleIdAsync(HistoryCategory.Succeeded));
        Assert.Equal(2, await SingleIdAsync(HistoryCategory.Failed));
        Assert.Equal(3, await SingleIdAsync(HistoryCategory.Cancelled));
        Assert.Equal(4, await SingleIdAsync(HistoryCategory.Retried));
        Assert.Equal(5, await SingleIdAsync(HistoryCategory.Skipped));
    }

    [Fact]
    public async Task Stats_CountOnlyFinishedJobsInTheSuccessRate()
    {
        await AddHistoryAsync(1, XactJobStatus.Completed);
        await AddHistoryAsync(2, XactJobStatus.Completed);
        await AddHistoryAsync(3, XactJobStatus.Completed);
        await AddHistoryAsync(4, XactJobStatus.Cancelled, stackTrace: "at Somewhere()");
        await AddHistoryAsync(5, XactJobStatus.Cancelled);
        await AddHistoryAsync(6, XactJobStatus.Failed, stackTrace: "at Somewhere()");
        await AddHistoryAsync(7, XactJobStatus.Failed, stackTrace: "at Somewhere()");

        await AddJobAsync(DateTime.UtcNow.AddMinutes(-1));
        await AddJobAsync(DateTime.UtcNow.AddHours(1));
        await AddJobAsync(DateTime.UtcNow.AddMinutes(-1), leasedUntil: DateTime.UtcNow.AddMinutes(5));

        var stats = await WithServiceAsync(s => s.GetDashboardStatsAsync());

        Assert.Equal(3, stats.SucceededLast24h);
        Assert.Equal(1, stats.FailedLast24h);
        Assert.Equal(2, stats.RetriedLast24h);
        Assert.Equal(75, stats.SuccessRateLast24h);
        Assert.Equal(1, stats.DueJobs);
        Assert.Equal(1, stats.ScheduledJobs);
        Assert.Equal(1, stats.ProcessingJobs);
    }

    [Fact]
    public async Task Paging_StartsAtTheFirstPage_AndLimitsThePageSize()
    {
        await AddJobAsync(DateTime.UtcNow);

        var result = await WithServiceAsync(s => s.GetScheduledJobsAsync(0, 1_000_000));

        Assert.Equal(1, result.Page);
        Assert.Equal(XactJobsUIService<UiTestDbContext>.MaxPageSize, result.PageSize);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task Chart_CountsEachIntervalInTheDatabase()
    {
        var from = new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

        await AddHistoryAsync(1, XactJobStatus.Completed, processedAt: from.AddMinutes(5));
        await AddHistoryAsync(2, XactJobStatus.Completed, processedAt: from.AddMinutes(65));
        await AddHistoryAsync(3, XactJobStatus.Cancelled, stackTrace: "at Somewhere()", processedAt: from.AddMinutes(70));
        await AddHistoryAsync(4, XactJobStatus.Cancelled, processedAt: from.AddMinutes(75));
        await AddHistoryAsync(5, XactJobStatus.Failed, stackTrace: "at Somewhere()", processedAt: from.AddMinutes(80));
        await AddHistoryAsync(6, XactJobStatus.Completed, processedAt: from.AddHours(3)); // outside the range

        var counts = await WithServiceAsync(s => s.GetJobCountsOverTimeAsync(from, from.AddHours(2), TimeSpan.FromHours(1)));

        Assert.Equal(2, counts.Count);

        Assert.Equal(from, counts[0].Timestamp);
        Assert.Equal((1, 0, 0, 0), (counts[0].Succeeded, counts[0].Failed, counts[0].Cancelled, counts[0].Retried));

        Assert.Equal(from.AddHours(1), counts[1].Timestamp);
        Assert.Equal((1, 1, 1, 1), (counts[1].Succeeded, counts[1].Failed, counts[1].Cancelled, counts[1].Retried));
    }

    [Fact]
    public async Task Chart_LimitsTheNumberOfIntervals()
    {
        var to = DateTime.UtcNow;

        var counts = await WithServiceAsync(s => s.GetJobCountsOverTimeAsync(to.AddDays(-31), to, TimeSpan.FromMinutes(1)));

        Assert.InRange(counts.Count, 1, XactJobsUIService<UiTestDbContext>.MaxChartIntervals);
    }

    [Fact]
    public async Task Queues_StartWithTheDefault_AndIncludeConfiguredAndUsedQueues()
    {
        await AddJobAsync(DateTime.UtcNow, queue: "emails");
        await AddJobAsync(DateTime.UtcNow, queue: QueueNames.Priority);

        var options = new XactJobsOptions<UiTestDbContext>();
        options.IsolatedQueues["reports"] = new XactJobsOptionsBase<UiTestDbContext>();

        await using var db = CreateDb();

        var queues = await new XactJobsUIService<UiTestDbContext>(db, jobsOptions: options).GetQueuesAsync();

        Assert.Equal([QueueNames.Default, "emails", QueueNames.Priority, "reports"], queues);
    }

    private UiTestDbContext CreateDb()
    {
        return new UiTestDbContext(new DbContextOptionsBuilder<UiTestDbContext>().UseSqlite(_connection).Options);
    }

    private async Task<T> WithServiceAsync<T>(Func<IXactJobsUIService, Task<T>> action)
    {
        await using var db = CreateDb();

        return await action(new XactJobsUIService<UiTestDbContext>(db));
    }

    private async Task<long> AddJobAsync(DateTime scheduledAt, int errorCount = 0, DateTime? leasedUntil = null, string queue = QueueNames.Default)
    {
        await using var db = CreateDb();

        var job = new XactJob(0, scheduledAt, typeof(SampleJobs).AssemblyQualifiedName!, nameof(SampleJobs.Run), "[]", queue,
                              errorCount: errorCount, leaser: leasedUntil.HasValue ? Guid.NewGuid() : null, leasedUntil: leasedUntil);

        db.Jobs.Add(job);

        await db.SaveChangesAsync();

        return job.Id;
    }

    private async Task AddHistoryAsync(long id, XactJobStatus status, string? stackTrace = null, string? periodicJobId = null, DateTime? processedAt = null)
    {
        await using var db = CreateDb();

        db.JobHistory.Add(new XactJobHistory(id, processedAt ?? DateTime.UtcNow.AddMinutes(-5), status, DateTime.UtcNow.AddMinutes(-6),
                                             typeof(SampleJobs).AssemblyQualifiedName!, nameof(SampleJobs.Run), "[]", QueueNames.Default,
                                             periodicJobId, periodicJobId != null ? Daily : null, periodicJobId != null ? 5 : null,
                                             errorCount: stackTrace != null ? 3 : 0,
                                             errorMessage: stackTrace != null ? "Boom" : "Cancelled",
                                             errorStackTrace: stackTrace));

        await db.SaveChangesAsync();
    }

    private async Task EnsurePeriodicAsync()
    {
        await using var db = CreateDb();

        await db.JobEnsurePeriodicAsync<SampleJobs>(x => x.Run(), PeriodicId, Daily, CancellationToken.None);

        await db.SaveChangesAsync();
    }

    private async Task<XactJob> GetPeriodicRunAsync()
    {
        await using var db = CreateDb();

        return await db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == PeriodicId);
    }

    private async Task<int> CountJobsAsync()
    {
        await using var db = CreateDb();

        return await db.Jobs.CountAsync();
    }

    private async Task<int> CountHistoryAsync()
    {
        await using var db = CreateDb();

        return await db.JobHistory.CountAsync();
    }
}
