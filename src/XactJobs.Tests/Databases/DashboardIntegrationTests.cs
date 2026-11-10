using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XactJobs.Tests.Postgres;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.Tests.Databases;

// The class names start with the database, so a filter like "XactJobs.Tests.Databases.Oracle" runs both suites of one database.

[Collection(OfficialMySqlCollection.Name)]
public sealed class OfficialMySqlDashboardTests(OfficialMySqlFixture fixture) : DashboardIntegrationTests(fixture);

#if !NET10_0_OR_GREATER // Pomelo doesn't support EF Core 10 yet
[Collection(PomeloMySqlCollection.Name)]
public sealed class PomeloMySqlDashboardTests(PomeloMySqlFixture fixture) : DashboardIntegrationTests(fixture);
#endif

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerDashboardTests(SqlServerFixture fixture) : DashboardIntegrationTests(fixture);

[Collection(OracleCollection.Name)]
public sealed class OracleDashboardTests(OracleFixture fixture) : DashboardIntegrationTests(fixture);

[Collection(Postgres16Collection.Name)]
public sealed class Postgres16DashboardTests(Postgres16Fixture fixture) : DashboardIntegrationTests(fixture);

[Collection(Postgres17Collection.Name)]
public sealed class Postgres17DashboardTests(Postgres17Fixture fixture) : DashboardIntegrationTests(fixture);

[Collection(Postgres18Collection.Name)]
public sealed class Postgres18DashboardTests(Postgres18Fixture fixture) : DashboardIntegrationTests(fixture);

/// <summary>
/// The dashboard's queries and actions on every database. The actions replace job rows by their whole primary key
/// (the scheduled time is part of it), so they depend on how each database stores the times.
/// </summary>
public abstract class DashboardIntegrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string Daily = "0 0 0 * * *";
    private const string PeriodicId = "dashboard-daily";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private readonly List<IHost> _hosts = [];

    public async Task InitializeAsync()
    {
        if (DatabaseFixture.IsDockerAvailable)
        {
            await fixture.PrepareForTestAsync();
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [DockerFact]
    public async Task Cancel_MovesAPendingJobToTheHistory_ButNotARunningOneNorAPeriodicRun()
    {
        var pendingId = await AddJobAsync(DateTime.UtcNow.AddHours(1), errorCount: 2);
        var runningId = await AddJobAsync(DateTime.UtcNow.AddMinutes(-1), leasedUntil: DateTime.UtcNow.AddMinutes(5));

        await EnsurePeriodicAsync();
        var periodicRun = await GetPeriodicRunAsync();

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.CancelJobAsync(KeyOf(pendingId), "alice"))).Status);
        Assert.Equal(ActionStatus.Conflict, (await WithServiceAsync(s => s.CancelJobAsync(KeyOf(runningId), null))).Status);
        Assert.Equal(ActionStatus.Conflict, (await WithServiceAsync(s => s.CancelJobAsync(KeyOf(periodicRun.Id), null))).Status);

        await using var db = CreateDb();

        Assert.False(await db.Jobs.AnyAsync(j => j.Id == pendingId));
        Assert.True(await db.Jobs.AnyAsync(j => j.Id == runningId));
        Assert.True(await db.Jobs.AnyAsync(j => j.Id == periodicRun.Id));

        var history = await db.JobHistory.AsNoTracking().SingleAsync();

        Assert.Equal(pendingId, history.Id);
        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.Equal(2, history.ErrorCount);
        Assert.Null(history.ErrorStackTrace);

        var cancelled = await WithServiceAsync(s => s.GetHistoryAsync(HistoryCategory.Cancelled, 1, 20));

        Assert.Equal(pendingId, Assert.Single(cancelled.Items).Id);
    }

    [DockerFact]
    public async Task RunNow_MovesAFutureJobToNow_ButNotARunningOne()
    {
        var futureId = await AddJobAsync(DateTime.UtcNow.AddHours(1), errorCount: 1);
        var running = DateTime.UtcNow.AddHours(1);
        var runningId = await AddJobAsync(running, leasedUntil: DateTime.UtcNow.AddMinutes(5));

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.RunJobNowAsync(KeyOf(futureId)))).Status);
        Assert.Equal(ActionStatus.Conflict, (await WithServiceAsync(s => s.RunJobNowAsync(KeyOf(runningId)))).Status);

        await using var db = CreateDb();

        var jobs = await db.Jobs.AsNoTracking().ToListAsync();

        Assert.Equal(2, jobs.Count);

        // the same row, moved to now (an UPDATE of the primary key): it keeps its id
        var moved = Assert.Single(jobs, j => j.Id == futureId);

        Assert.True(moved.ScheduledAt <= DateTime.UtcNow);
        Assert.Equal(1, moved.ErrorCount);
        Assert.True(jobs.Single(j => j.Id == runningId).ScheduledAt > DateTime.UtcNow);
    }

    [DockerFact]
    public async Task Trigger_RunsThePeriodicJobOnce_AndItKeepsOneChainOfRuns()
    {
        await EnsurePeriodicAsync();

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.TriggerPeriodicJobAsync(PeriodicId))).Status);
        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.TriggerPeriodicJobAsync(PeriodicId))).Status);

        var triggered = await GetPeriodicRunAsync();

        Assert.True(triggered.ScheduledAt <= DateTime.UtcNow);

        var host = CreateHost();

        await host.StartAsync();

        await WaitForAsync(() => WithDbAsync(db => db.JobHistory.AnyAsync(h => h.Id == triggered.Id)), "the triggered run");

        // the runner scheduled the next run, and only that one
        var next = await GetPeriodicRunAsync();

        Assert.True(next.ScheduledAt > DateTime.UtcNow);
        Assert.Equal(XactJobStatus.Completed, (await WithDbAsync(db => db.JobHistory.AsNoTracking().SingleAsync(h => h.Id == triggered.Id))).Status);
    }

    [DockerFact]
    public async Task Trigger_IsRefusedWhenRunningOrPaused()
    {
        await EnsurePeriodicAsync();

        await WithDbAsync(db => db.Jobs.Where(j => j.PeriodicJobId == PeriodicId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeasedUntil, DateTime.UtcNow.AddMinutes(5))));

        Assert.Equal(ActionStatus.Conflict, (await WithServiceAsync(s => s.TriggerPeriodicJobAsync(PeriodicId))).Status);

        await WithDbAsync(db => db.Jobs.Where(j => j.PeriodicJobId == PeriodicId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.LeasedUntil, (DateTime?)null)));

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.SetPeriodicJobActiveAsync(PeriodicId, false))).Status);
        Assert.Equal(ActionStatus.Conflict, (await WithServiceAsync(s => s.TriggerPeriodicJobAsync(PeriodicId))).Status);
        Assert.Equal(0, await WithDbAsync(db => db.Jobs.CountAsync()));
    }

    [DockerFact]
    public async Task PauseResumeAndDelete_AreSaved()
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

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.DeletePeriodicJobAsync(PeriodicId))).Status);
        Assert.False(await WithDbAsync(db => db.PeriodicJobs.AnyAsync()));
    }

    [DockerFact]
    public async Task Retry_EnqueuesAFailedPeriodicRunAsAOneOffJob_AndRefusesARetriedAttempt()
    {
        await AddHistoryAsync(1, XactJobStatus.Cancelled, DateTime.UtcNow.AddMinutes(-5), stackTrace: "at Somewhere()", periodicJobId: PeriodicId);
        await AddHistoryAsync(2, XactJobStatus.Failed, DateTime.UtcNow.AddMinutes(-5), stackTrace: "at Somewhere()");

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.RetryJobAsync(1))).Status);
        Assert.Equal(ActionStatus.Conflict, (await WithServiceAsync(s => s.RetryJobAsync(2))).Status);

        var job = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync());

        Assert.Null(job.PeriodicJobId);
        Assert.Equal(0, job.ErrorCount);
    }

    [DockerFact]
    public async Task HistoryStatsChartAndQueues_AreCountedInTheDatabase()
    {
        var hour = DateTime.UtcNow.AddHours(-3);
        hour = new DateTime(hour.Year, hour.Month, hour.Day, hour.Hour, 0, 0, DateTimeKind.Utc);

        await AddHistoryAsync(1, XactJobStatus.Completed, hour.AddMinutes(5));
        await AddHistoryAsync(2, XactJobStatus.Completed, hour.AddMinutes(65));
        await AddHistoryAsync(3, XactJobStatus.Cancelled, hour.AddMinutes(70), stackTrace: "at Somewhere()");
        await AddHistoryAsync(4, XactJobStatus.Cancelled, hour.AddMinutes(75));
        await AddHistoryAsync(5, XactJobStatus.Failed, hour.AddMinutes(80), stackTrace: "at Somewhere()");
        await AddHistoryAsync(6, XactJobStatus.Skipped, hour.AddMinutes(85));

        await AddJobAsync(DateTime.UtcNow.AddMinutes(-1), queue: "emails");
        await AddJobAsync(DateTime.UtcNow.AddHours(1));
        await AddJobAsync(DateTime.UtcNow.AddMinutes(-1), leasedUntil: DateTime.UtcNow.AddMinutes(5));

        async Task<long> SingleIdAsync(HistoryCategory category) =>
            Assert.Single((await WithServiceAsync(s => s.GetHistoryAsync(category, 1, 20))).Items).Id;

        Assert.Equal(3, await SingleIdAsync(HistoryCategory.Failed));
        Assert.Equal(4, await SingleIdAsync(HistoryCategory.Cancelled));
        Assert.Equal(5, await SingleIdAsync(HistoryCategory.Retried));
        Assert.Equal(6, await SingleIdAsync(HistoryCategory.Skipped));
        Assert.Equal(2, (await WithServiceAsync(s => s.GetHistoryAsync(HistoryCategory.Succeeded, 1, 20))).TotalCount);

        var stats = await WithServiceAsync(s => s.GetDashboardStatsAsync());

        Assert.Equal((2, 1, 1), (stats.SucceededLast24h, stats.FailedLast24h, stats.RetriedLast24h));
        Assert.Equal((1, 1, 1), (stats.DueJobs, stats.ScheduledJobs, stats.ProcessingJobs));

        var chart = await WithServiceAsync(s => s.GetJobCountsOverTimeAsync(hour, hour.AddHours(2), TimeSpan.FromHours(1)));

        Assert.Equal(2, chart.Count);
        Assert.Equal((1, 0, 0, 0, 0), (chart[0].Succeeded, chart[0].Failed, chart[0].Cancelled, chart[0].Retried, chart[0].Skipped));
        Assert.Equal((1, 1, 1, 1, 1), (chart[1].Succeeded, chart[1].Failed, chart[1].Cancelled, chart[1].Retried, chart[1].Skipped));

        var queues = await WithServiceAsync(s => s.GetQueuesAsync());

        Assert.Equal([QueueNames.Default, "emails"], queues);

        var emails = await WithServiceAsync(s => s.GetScheduledJobsAsync(1, 20, "emails"));

        Assert.Equal("emails", Assert.Single(emails.Items).Queue);
    }

    [DockerFact]
    public async Task Batches_AreListedWithTheirCounts_AndShownWithTheirJobsAndContinuations()
    {
        var finished = Guid.NewGuid();
        var typeName = typeof(DashboardIntegrationTests).AssemblyQualifiedName!;

        var (active, continuation) = await WithDbAsync(async db =>
        {
            var batch = db.JobBatch()
                .ScheduleIn<DashboardIntegrationTests>(x => x.GetHashCode(), TimeSpan.FromHours(1), "not-polled")
                .ScheduleIn<DashboardIntegrationTests>(x => x.GetHashCode(), TimeSpan.FromHours(2), "not-polled");

            var continuation = batch.ContinueWith<DashboardIntegrationTests>(x => x.GetHashCode(), XactJobContinuation.OnFailure, "not-polled");

            XactJobHistory History(long id, XactJobStatus status, Guid? batchId, Guid? awaitsBatchId = null, int minutesAgo = 5) =>
                new(id, DateTime.UtcNow.AddMinutes(-minutesAgo), status, DateTime.UtcNow.AddMinutes(-minutesAgo - 1), typeName, "Run()", "[]", QueueNames.Default)
                {
                    BatchId = batchId,
                    AwaitsBatchId = awaitsBatchId
                };

            db.JobHistory.AddRange(
                History(11, XactJobStatus.Completed, batch.Id),
                History(12, XactJobStatus.Cancelled, batch.Id),
                History(13, XactJobStatus.Failed, batch.Id),
                History(21, XactJobStatus.Completed, finished, minutesAgo: 30),
                History(22, XactJobStatus.Skipped, finished, minutesAgo: 20),
                History(31, XactJobStatus.Skipped, null, awaitsBatchId: finished, minutesAgo: 10));

            await db.SaveChangesAsync();

            return (batch.Id, continuation.Id);
        });

        var activeBatch = Assert.Single((await WithServiceAsync(s => s.GetBatchesAsync(BatchState.Active, 1, 20))).Items);

        Assert.Equal(active, activeBatch.Id);
        Assert.Equal(2, activeBatch.PendingJobs);
        Assert.Equal(1, activeBatch.SucceededJobs);
        Assert.Equal(1, activeBatch.FailedJobs);
        Assert.Equal(1, activeBatch.AwaitingContinuations);
        Assert.NotNull(activeBatch.NextRunAt);
        Assert.True(activeBatch.NextRunAt < DateTime.UtcNow.AddHours(1.5), "The next run is the earliest one");
        Assert.NotNull(activeBatch.TypeName);

        var finishedBatch = Assert.Single((await WithServiceAsync(s => s.GetBatchesAsync(BatchState.Finished, 1, 20))).Items);

        Assert.Equal(finished, finishedBatch.Id);
        Assert.Equal(1, finishedBatch.SucceededJobs);
        Assert.Equal(1, finishedBatch.FailedJobs);
        Assert.Equal(typeName, finishedBatch.TypeName);

        var details = await WithServiceAsync(s => s.GetBatchAsync(active));

        Assert.NotNull(details);
        Assert.Equal(2, details.Jobs.Count);
        Assert.Equal(3, details.History.Count);

        var waiting = Assert.Single(details.PendingContinuations);

        Assert.Equal(continuation, waiting.Id);
        Assert.True(waiting.IsAwaitingBatch);
        Assert.Equal(2, waiting.BatchJobsLeft);

        var finishedDetails = await WithServiceAsync(s => s.GetBatchAsync(finished));

        Assert.NotNull(finishedDetails);
        Assert.Equal(31, Assert.Single(finishedDetails.FinishedContinuations).Id);

        var stats = await WithServiceAsync(s => s.GetDashboardStatsAsync());

        Assert.Equal(2, stats.ScheduledJobs);
        Assert.Equal(1, stats.AwaitingContinuations);

        Assert.Equal(ActionStatus.Done, (await WithServiceAsync(s => s.RetryJobAsync(31))).Status);
    }

    private TestDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>();

        fixture.Configure(options, retryOnFailure: false);

        return new TestDbContext(options.Options);
    }

    /// <summary>
    /// The primary key of a pending job, as the dashboard has it from the job's row (the stored time, with the database's precision).
    /// </summary>
    private JobKey KeyOf(long id)
    {
        using var db = CreateDb();

        return JobKey.Of(db.Jobs.AsNoTracking().Single(j => j.Id == id));
    }

    private async Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        await using var db = CreateDb();

        return await action(db);
    }

    private async Task<T> WithServiceAsync<T>(Func<IXactJobsUIService, Task<T>> action)
    {
        await using var db = CreateDb();

        return await action(new XactJobsUIService<TestDbContext>(db));
    }

    private Task<long> AddJobAsync(DateTime scheduledAt, int errorCount = 0, DateTime? leasedUntil = null, string queue = QueueNames.Default)
    {
        return WithDbAsync(async db =>
        {
            var job = new XactJob(0, scheduledAt, typeof(DashboardJob).AssemblyQualifiedName!, nameof(DashboardJob.Run), "[]", queue,
                                  errorCount: errorCount, leaser: leasedUntil.HasValue ? Guid.NewGuid() : null, leasedUntil: leasedUntil);

            db.Jobs.Add(job);

            await db.SaveChangesAsync();

            return job.Id;
        });
    }

    private Task AddHistoryAsync(long id, XactJobStatus status, DateTime processedAt, string? stackTrace = null, string? periodicJobId = null)
    {
        return WithDbAsync(async db =>
        {
            db.JobHistory.Add(new XactJobHistory(id, processedAt, status, processedAt.AddMinutes(-1),
                                                 typeof(DashboardJob).AssemblyQualifiedName!, nameof(DashboardJob.Run), "[]", QueueNames.Default,
                                                 periodicJobId, periodicJobId != null ? Daily : null, periodicJobId != null ? 5 : null,
                                                 errorCount: stackTrace != null ? 3 : 0,
                                                 errorMessage: stackTrace != null ? "Boom" : "Cancelled",
                                                 errorStackTrace: stackTrace));

            return await db.SaveChangesAsync();
        });
    }

    private Task EnsurePeriodicAsync()
    {
        return WithDbAsync(async db =>
        {
            await db.JobEnsurePeriodicAsync<DashboardJob>(x => x.Run(), PeriodicId, Daily, CancellationToken.None);

            return await db.SaveChangesAsync();
        });
    }

    private Task<XactJob> GetPeriodicRunAsync()
    {
        return WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == PeriodicId));
    }

    private IHost CreateHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<TestDbContext>(options => fixture.Configure(options, retryOnFailure: false));
        builder.Services.AddXactJobs<TestDbContext>(options => options.WithWorkerCount(1).WithPollingInterval(1).WithShutdownTimeout(0));
        builder.Services.AddScoped<DashboardJob>();

        var host = builder.Build();

        _hosts.Add(host);

        return host;
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

    public sealed class DashboardJob
    {
        public void Run()
        {
        }
    }
}
