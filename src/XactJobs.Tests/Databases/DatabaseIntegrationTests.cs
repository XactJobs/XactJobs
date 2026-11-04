using System.Collections.Concurrent;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XactJobs.Tests.Postgres;

namespace XactJobs.Tests.Databases;

[Collection(OfficialMySqlCollection.Name)]
public sealed class OfficialMySqlIntegrationTests(OfficialMySqlFixture fixture) : MySqlIntegrationTests(fixture);

#if !NET10_0_OR_GREATER // Pomelo doesn't support EF Core 10 yet
[Collection(PomeloMySqlCollection.Name)]
public sealed class PomeloMySqlIntegrationTests(PomeloMySqlFixture fixture) : MySqlIntegrationTests(fixture);
#endif

public abstract class MySqlIntegrationTests(MySqlFixtureBase fixture) : DatabaseIntegrationTests(fixture)
{
    /// <summary>
    /// For the EF migrations route: the database decides if the schema from the model is valid.
    /// The tables are renamed, because the database already has the ones from the SQL scripts.
    /// </summary>
    [DockerFact]
    public async Task SchemaGeneratedFromTheModel_IsAcceptedByTheDatabase()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>();

        fixture.Configure(options, retryOnFailure: false);

        await using var db = new TestDbContext(options.Options);

        var script = db.Database.GenerateCreateScript().Replace("xact_jobs_", "model_check_");

        Assert.Contains("model_check_job", script);

        try
        {
            await db.Database.ExecuteSqlRawAsync(script);

            await db.Database.ExecuteSqlRawAsync("INSERT INTO model_check_job (scheduled_at, queue, type_name, method_name, method_args, error_count) VALUES (UTC_TIMESTAMP(6), 'q', 'T', 'M', '[]', 0)");
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS model_check_job, model_check_job_history, model_check_job_periodic");
        }
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerIntegrationTests(SqlServerFixture fixture) : DatabaseIntegrationTests(fixture);

[Collection(OracleCollection.Name)]
public sealed class OracleIntegrationTests(OracleFixture fixture) : DatabaseIntegrationTests(fixture);

[Collection(PostgresCollection.Name)]
public sealed class PostgresIntegrationTests(PostgresFixture fixture) : DatabaseIntegrationTests(fixture);

/// <summary>
/// What every database has to do the same way, with the real runner and the real SQL scripts.
/// </summary>
public abstract class DatabaseIntegrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private const string Daily = "0 0 0 * * *";
    private const string NotPolled = "not-polled";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private readonly List<IHost> _hosts = [];
    private readonly IntegrationJobState _state = new();
    private readonly LogCapture _logs = new();

    public async Task InitializeAsync()
    {
        if (DatabaseFixture.IsDockerAvailable)
        {
            await fixture.PrepareForTestAsync();
        }
    }

    public async Task DisposeAsync()
    {
        _state.Release();

        foreach (var host in _hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    [DockerFact]
    public async Task MigrationScripts_RemoveTheLeaserIndex()
    {
        var host = CreateHost();

        var count = await WithDbAsync(host, async db =>
        {
            await db.Database.OpenConnectionAsync();

            await using var command = db.Database.GetDbConnection().CreateCommand();

            command.CommandText = fixture.CountLeaserIndexesSql;

            return Convert.ToInt32(await command.ExecuteScalarAsync());
        });

        Assert.Equal(0, count);
    }

    [DockerFact]
    public async Task Claim_TreatsSpecialCharactersInQueueNameAsData()
    {
        var host = CreateHost();

        const string queueName = "o'brien\"; --";

        await WithDbAsync(host, db =>
        {
            db.Jobs.Add(NewRow(queueName));
            db.Jobs.Add(NewRow(NotPolled));

            return db.SaveChangesAsync();
        });

        var claimed = await ClaimAsync(host, queueName, 10, Guid.NewGuid(), 60);

        Assert.Equal(queueName, Assert.Single(claimed).Queue);
    }

    [DockerFact]
    public async Task ClaimAndExtend_KeepFractionsOfASecondInTheLease()
    {
        var host = CreateHost();

        await WithDbAsync(host, db =>
        {
            db.Jobs.Add(NewRow(NotPolled));

            return db.SaveChangesAsync();
        });

        var leaser = Guid.NewGuid();

        var claimed = await ClaimAsync(host, NotPolled, 1, leaser, 60);

        AssertHasFraction(await GetLeaseAsync(host, claimed[0].Id));

        await ExecuteAsync(host, fixture.Dialect.GetExtendLeaseSql(leaser, claimed, 60));

        AssertHasFraction(await GetLeaseAsync(host, claimed[0].Id));

        static void AssertHasFraction(DateTime lease) => Assert.NotEqual(0, lease.Ticks % TimeSpan.TicksPerSecond);
    }

    [DockerFact]
    public async Task LeaseSql_OnlyTouchesGivenJobsStillLeasedByTheLeaser()
    {
        var host = CreateHost();

        await WithDbAsync(host, db =>
        {
            for (var i = 0; i < 3; i++)
            {
                db.Jobs.Add(new XactJob(0, DateTime.UtcNow.AddMinutes(-1).AddTicks(i * 13), "Type", "Method", "[]", NotPolled));
            }

            return db.SaveChangesAsync();
        });

        var leaser = Guid.NewGuid();

        var claimed = await ClaimAsync(host, NotPolled, 3, leaser, 60);

        Assert.Equal(3, claimed.Count);

        IReadOnlyCollection<XactJob> two = [claimed[0], claimed[1]];

        Assert.Equal(0, await ExecuteAsync(host, fixture.Dialect.GetExtendLeaseSql(Guid.NewGuid(), two, 3600)));
        Assert.Equal(2, await ExecuteAsync(host, fixture.Dialect.GetExtendLeaseSql(leaser, two, 3600)));

        var cutoff = DateTime.UtcNow.AddMinutes(30);

        Assert.True(await GetLeaseAsync(host, claimed[0].Id) > cutoff);
        Assert.True(await GetLeaseAsync(host, claimed[1].Id) > cutoff);
        Assert.True(await GetLeaseAsync(host, claimed[2].Id) < cutoff);

        Assert.Equal(0, await ExecuteAsync(host, fixture.Dialect.GetClearLeaseSql(Guid.NewGuid(), two)));
        Assert.Equal(2, await ExecuteAsync(host, fixture.Dialect.GetClearLeaseSql(leaser, two)));

        Assert.Equal(2, await WithDbAsync(host, db => db.Jobs.CountAsync(j => j.Leaser == null)));
    }

    [DockerFact]
    public async Task ParallelFailures_EachGetErrorCountHistoryAndReschedule()
    {
        const int jobCount = 4;

        var retryAt = DateTime.UtcNow.AddHours(1);

        var host = CreateHost(options => options.WithMaxDegreeOfParallelism(jobCount).WithRetryStrategy(new FixedRetryStrategy(retryAt)));

        var jobIds = await WithDbAsync(host, async db =>
        {
            var jobs = Enumerable.Range(0, jobCount)
                .Select(_ => db.JobEnqueue<IntegrationJob>(x => x.FailTogetherAsync(jobCount)))
                .ToList();

            await db.SaveChangesAsync();

            return jobs.Select(j => j.Id).Order().ToList();
        });

        await host.StartAsync();

        await WaitForAsync(() => WithDbAsync(host, async db => await db.JobHistory.CountAsync() >= jobCount), "history rows");

        var history = await WithDbAsync(host, db => db.JobHistory.AsNoTracking().ToListAsync());

        Assert.Equal(jobIds, history.Select(h => h.Id).Order());
        Assert.All(history, h =>
        {
            Assert.Equal(XactJobStatus.Failed, h.Status);
            Assert.Equal(1, h.ErrorCount);
        });

        var pending = await WithDbAsync(host, db => db.Jobs.AsNoTracking().Where(j => j.PeriodicJobId == null).ToListAsync());

        Assert.Equal(jobCount, pending.Count);
        Assert.All(pending, j =>
        {
            Assert.Equal(1, j.ErrorCount);
            Assert.Equal(retryAt, j.ScheduledAt, TimeSpan.FromSeconds(1));
        });
    }

    [DockerFact]
    public async Task CancelledJob_IsNotRetried_AndLosesItsWrites()
    {
        // a strategy that would retry
        var host = CreateHost(options => options.WithRetryStrategy(new FixedRetryStrategy(DateTime.UtcNow.AddHours(1))));

        var jobId = await EnqueueAsync(host, x => x.WriteAndCancelAsync());

        await host.StartAsync();

        await WaitForAsync(() => WithDbAsync(host, db => db.JobHistory.AnyAsync(h => h.Id == jobId)), "the cancel to be recorded");

        var history = await WithDbAsync(host, db => db.JobHistory.AsNoTracking().SingleAsync(h => h.Id == jobId));

        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.Equal(1, history.ErrorCount);
        Assert.Equal(IntegrationJob.CancelReason, history.ErrorMessage);
        Assert.NotNull(history.ErrorStackTrace);

        Assert.False(await WithDbAsync(host, db => db.Jobs.AnyAsync(j => j.PeriodicJobId == null)));
        Assert.Equal(0, await CountMarkersAsync(host));
        Assert.False(_logs.HasErrors, _logs.ToString());
    }

    [DockerFact]
    public async Task Failure_WithLongErrorDetails_IsRecordedInFull()
    {
        const int messageLength = 5000;

        var host = CreateHost();

        var jobId = await EnqueueAsync(host, x => x.FailWithLongDetailsAsync(messageLength));

        await host.StartAsync();

        await WaitForAsync(() => WithDbAsync(host, db => db.JobHistory.AnyAsync(h => h.Id == jobId)), "the failure to be recorded");

        var history = await WithDbAsync(host, db => db.JobHistory.AsNoTracking().SingleAsync(h => h.Id == jobId));

        Assert.Equal(XactJobStatus.Cancelled, history.Status);
        Assert.Equal(messageLength, history.ErrorMessage!.Length);
        Assert.True(history.ErrorStackTrace!.Length > 2000, $"Stack trace has only {history.ErrorStackTrace.Length} characters");
    }

    [DockerFact]
    public async Task RunningJob_KeepsItsLeaseExtended_AndNoOtherWorkerClaimsIt()
    {
        const int leaseDurationInSeconds = 3;

        var host = CreateHost(options => options.WithWorkerCount(2).WithLeaseDuration(leaseDurationInSeconds));

        var jobId = await EnqueueAsync(host, x => x.BlockUntilCancelledAsync(CancellationToken.None));

        await host.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 1), "the job to start");

        var firstLease = await GetLeaseAsync(host, jobId);

        await Task.Delay(TimeSpan.FromSeconds(leaseDurationInSeconds + 2));

        Assert.True(await GetLeaseAsync(host, jobId) > firstLease);
        Assert.Equal(1, _state.BlockedCount);
    }

    [DockerFact]
    public async Task Stop_ClearsLeasesOfRunningJobs_ButNotOfOtherLeasers()
    {
        var host = CreateHost(options => options.WithMaxDegreeOfParallelism(2));

        var jobIds = new[]
        {
            await EnqueueAsync(host, x => x.BlockUntilCancelledAsync(CancellationToken.None)),
            await EnqueueAsync(host, x => x.BlockUntilCancelledAsync(CancellationToken.None)),
        };

        var otherId = await WithDbAsync(host, async db =>
        {
            var other = new XactJob(0, DateTime.UtcNow.AddMinutes(-1), "Type", "Method", "[]", QueueNames.Default,
                                    leaser: Guid.NewGuid(), leasedUntil: DateTime.UtcNow.AddHours(1));

            db.Jobs.Add(other);

            await db.SaveChangesAsync();

            return other.Id;
        });

        await host.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 2), "the jobs to start");

        await host.StopAsync();

        var jobs = await WithDbAsync(host, db => db.Jobs.AsNoTracking().Where(j => jobIds.Contains(j.Id)).ToListAsync());

        Assert.Equal(2, jobs.Count);
        Assert.All(jobs, j =>
        {
            Assert.Null(j.Leaser);
            Assert.Null(j.LeasedUntil);
            Assert.Equal(0, j.ErrorCount);
        });

        var untouched = await WithDbAsync(host, db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == otherId));

        Assert.NotNull(untouched.Leaser);
    }

    [DockerFact]
    public async Task Stop_LetsRunningJobsFinishWithinTheTimeout()
    {
        var host = CreateHost(options => options.WithShutdownTimeout(30));

        var jobId = await EnqueueAsync(host, x => x.BlockUntilReleasedAsync());

        await host.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 1), "the job to start");

        var stopping = host.StopAsync();

        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.False(stopping.IsCompleted);

        _state.Release();

        await stopping;

        var history = await WithDbAsync(host, db => db.JobHistory.AsNoTracking().SingleAsync(h => h.Id == jobId));

        Assert.Equal(XactJobStatus.Completed, history.Status);
    }

    [DockerFact]
    public async Task SlowJob_DoesNotStopShortJobsOnTheSameWorker()
    {
        const int shortJobs = 20;

        var host = CreateHost(options => options.WithMaxDegreeOfParallelism(2));

        var slowJobId = await EnqueueAsync(host, x => x.BlockUntilCancelledAsync(CancellationToken.None));

        await host.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 1), "the slow job to start");

        // added now, so that they can only run if the worker keeps polling while the slow job is running
        await WithDbAsync(host, db =>
        {
            for (var i = 0; i < shortJobs; i++)
            {
                var number = i;

                db.JobEnqueue<IntegrationJob>(x => x.Count(number));
            }

            return db.SaveChangesAsync();
        });

        await WaitForAsync(() => Task.FromResult(_state.Runs.Count == shortJobs), "the short jobs to run");

        // their rows are removed when the worker commits, right after they run
        await WaitForAsync(() => WithDbAsync(host, async db => await db.Jobs.CountAsync(j => j.PeriodicJobId == null) == 1), "the short jobs to be committed");

        var left = await WithDbAsync(host, db => db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == null));

        Assert.Equal(slowJobId, left.Id);
        Assert.NotNull(left.Leaser);
    }

    [DockerFact]
    public async Task CompetingWorkers_RunEveryJobExactlyOnce()
    {
        const int jobCount = 100;

        var host = CreateHost(options => options.WithWorkerCount(4).WithMaxDegreeOfParallelism(4));

        await WithDbAsync(host, db =>
        {
            for (var i = 0; i < jobCount; i++)
            {
                var number = i;

                db.JobEnqueue<IntegrationJob>(x => x.Count(number));
            }

            return db.SaveChangesAsync();
        });

        await host.StartAsync();

        await WaitForAsync(() => WithDbAsync(host, async db => !await db.Jobs.AnyAsync(j => j.PeriodicJobId == null)), "all jobs to finish");

        Assert.Equal(jobCount, _state.Runs.Count);
        Assert.All(_state.Runs.Values, runs => Assert.Equal(1, runs));
        Assert.False(_logs.HasErrors, _logs.ToString());
    }

    [DockerFact]
    public async Task Reconciliation_SchedulesOnlyActiveJobsWithoutARunOfTheirCurrentVersion()
    {
        var host = CreateHost();

        var (missing, inactive, oldVersion, healthy, legacy) = await WithDbAsync(host, async db =>
        {
            var template = CreateTemplate(db);

            db.PeriodicJobs.AddRange(
                NewPeriodic(template, "missing"),
                NewPeriodic(template, "inactive", isActive: false),
                NewPeriodic(template, "old-version", version: 2),
                NewPeriodic(template, "healthy"),
                NewPeriodic(template, "legacy"));

            db.Jobs.AddRange(
                NewPeriodicRow(template, "old-version", 1),
                NewPeriodicRow(template, "healthy", 1),
                NewPeriodicRow(template, "legacy", null));

            await db.SaveChangesAsync();

            return ("missing", "inactive", "old-version", "healthy", "legacy");
        });

        await host.StartAsync();

        await WaitForAsync(() => WithDbAsync(host, db => db.Jobs.AnyAsync(j => j.PeriodicJobId == missing && j.PeriodicJobVersion == 1)), "the missing run");
        await WaitForAsync(() => WithDbAsync(host, db => db.Jobs.AnyAsync(j => j.PeriodicJobId == oldVersion && j.PeriodicJobVersion == 2)), "the current version");

        // a few more cycles
        await Task.Delay(TimeSpan.FromSeconds(3));

        Assert.Equal(1, await CountRowsAsync(host, missing));
        Assert.Equal(2, await CountRowsAsync(host, oldVersion));
        Assert.Equal(1, await CountRowsAsync(host, healthy));
        Assert.Equal(1, await CountRowsAsync(host, legacy));
        Assert.Equal(0, await CountRowsAsync(host, inactive));

        await WithDbAsync(host, db => db.Jobs.Where(j => j.PeriodicJobId == healthy).ExecuteDeleteAsync());

        await WaitForAsync(() => WithDbAsync(host, db => db.Jobs.AnyAsync(j => j.PeriodicJobId == healthy)), "the deleted run to be rescheduled");

        Assert.False(_logs.HasErrors, _logs.ToString());
    }

    [DockerFact]
    public async Task WithRetryingExecutionStrategy_PeriodicJobsFromOptionsAreCreated()
    {
        if (!fixture.SupportsRetryOnFailure) return;

        var host = CreateHost(options => options.WithPeriodicJob<IntegrationJob>(x => x.Run(), "from-options", Daily), retryOnFailure: true);

        await host.StartAsync();

        await WaitForAsync(() => WithDbAsync(host, db => db.Jobs.AnyAsync(j => j.PeriodicJobId == "from-options")), "the periodic job to be created");

        Assert.False(_logs.HasErrors, _logs.ToString());
    }

    [DockerFact]
    public async Task WithRetryingExecutionStrategy_JobsAreClaimedAndRun()
    {
        if (!fixture.SupportsRetryOnFailure) return;

        var host = CreateHost(retryOnFailure: true);

        await EnqueueAsync(host, x => x.CountAsync(1));

        await host.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.Runs.Count == 1), "the job to run");
    }

    [DockerFact]
    public async Task PeriodicJobDeletingItself_CompletesWithHistory_AndDoesNotRunAgain()
    {
        var host = CreateHost();

        var jobId = await WithDbAsync(host, async db =>
        {
            db.JobEnsurePeriodic<IntegrationJob>(x => x.DeletePeriodicAsync("self", CancellationToken.None), "self", Daily);

            await db.SaveChangesAsync();

            var pending = await db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == "self");

            await db.Jobs.Where(j => j.Id == pending.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.ScheduledAt, DateTime.UtcNow.AddMinutes(-1)));

            return pending.Id;
        });

        await host.StartAsync();

        await WaitForAsync(() => WithDbAsync(host, db => db.JobHistory.AnyAsync(h => h.Id == jobId)), "the history row");

        Assert.Equal(XactJobStatus.Completed, (await WithDbAsync(host, db => db.JobHistory.AsNoTracking().SingleAsync(h => h.Id == jobId))).Status);
        Assert.False(await WithDbAsync(host, db => db.PeriodicJobs.AnyAsync(p => p.Id == "self")));
        Assert.Equal(0, await CountRowsAsync(host, "self"));
    }

    [DockerFact]
    public async Task PausedJob_HasNoPendingRun_StaysPausedWhenEnsuredAgain_AndResumesWithOneRun()
    {
        var host = CreateHost();

        await WithDbAsync(host, async db =>
        {
            db.JobEnsurePeriodic<IntegrationJob>(x => x.Run(), "pausable", Daily);

            await db.SaveChangesAsync();

            Assert.True(await db.JobSetPeriodicActiveAsync("pausable", false, CancellationToken.None));

            return await db.SaveChangesAsync();
        });

        Assert.Equal(0, await CountRowsAsync(host, "pausable"));

        var periodicJob = await WithDbAsync(host, async db =>
        {
            var periodicJob = db.JobEnsurePeriodic<IntegrationJob>(x => x.Run(), "pausable", "0 0 * * * *");

            await db.SaveChangesAsync();

            return periodicJob;
        });

        Assert.False((await WithDbAsync(host, db => db.PeriodicJobs.AsNoTracking().SingleAsync(p => p.Id == "pausable"))).IsActive);
        Assert.Equal(0, await CountRowsAsync(host, "pausable"));

        await WithDbAsync(host, async db =>
        {
            Assert.True(await db.JobSetPeriodicActiveAsync("pausable", true, CancellationToken.None));

            return await db.SaveChangesAsync();
        });

        var run = await WithDbAsync(host, db => db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == "pausable"));

        Assert.Equal(periodicJob.Version, run.PeriodicJobVersion);
        Assert.True((await WithDbAsync(host, db => db.PeriodicJobs.AsNoTracking().SingleAsync(p => p.Id == "pausable"))).IsActive);
    }

    [DockerFact]
    public async Task JobPausedWhileRunning_Completes_AndDoesNotScheduleTheNextRun()
    {
        var host = CreateHost();

        var jobId = await WithDbAsync(host, async db =>
        {
            db.JobEnsurePeriodic<IntegrationJob>(x => x.BlockUntilReleasedAsync(), "paused-running", Daily);

            await db.SaveChangesAsync();

            var pending = await db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == "paused-running");

            await db.Jobs.Where(j => j.Id == pending.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.ScheduledAt, DateTime.UtcNow.AddMinutes(-1)));

            return pending.Id;
        });

        await host.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 1), "the job to start");

        await WithDbAsync(host, async db =>
        {
            Assert.True(await db.JobSetPeriodicActiveAsync("paused-running", false, CancellationToken.None));

            return await db.SaveChangesAsync();
        });

        // the running row is leased, so pausing keeps it
        Assert.Equal(1, await CountRowsAsync(host, "paused-running"));

        _state.Release();

        await WaitForAsync(() => WithDbAsync(host, db => db.JobHistory.AnyAsync(h => h.Id == jobId)), "the history row");

        Assert.Equal(XactJobStatus.Completed, (await WithDbAsync(host, db => db.JobHistory.AsNoTracking().SingleAsync(h => h.Id == jobId))).Status);
        Assert.Equal(0, await CountRowsAsync(host, "paused-running"));
        Assert.False(_logs.HasErrors, _logs.ToString());
    }

    [DockerFact]
    public async Task DeleteAndAddAgain_SkipsTheOldPendingRow_AndContinuesWithTheNewOne()
    {
        var host = CreateHost();

        var (oldJobId, newJobId, version) = await WithDbAsync(host, async db =>
        {
            db.JobEnsurePeriodic<IntegrationJob>(x => x.Run(), "again", Daily);

            await db.SaveChangesAsync();

            var oldJob = await db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == "again");

            Assert.True(await db.JobDeletePeriodicAsync("again", CancellationToken.None));

            await db.SaveChangesAsync();

            var periodicJob = db.JobEnsurePeriodic<IntegrationJob>(x => x.Run(), "again", Daily);

            await db.SaveChangesAsync();

            var newJob = await db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == "again" && j.Id != oldJob.Id);

            await db.Jobs.Where(j => j.PeriodicJobId == "again")
                .ExecuteUpdateAsync(s => s.SetProperty(j => j.ScheduledAt, DateTime.UtcNow.AddMinutes(-1)));

            return (oldJob.Id, newJob.Id, periodicJob.Version);
        });

        await host.StartAsync();

        await WaitForAsync(() => WithDbAsync(host, async db => await db.JobHistory.CountAsync(h => h.PeriodicJobId == "again") >= 2), "both rows to be processed");

        var history = await WithDbAsync(host, db => db.JobHistory.AsNoTracking().Where(h => h.PeriodicJobId == "again").ToDictionaryAsync(h => h.Id));

        Assert.Equal(XactJobStatus.Skipped, history[oldJobId].Status);
        Assert.Equal(XactJobStatus.Completed, history[newJobId].Status);

        var pending = await WithDbAsync(host, db => db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == "again"));

        Assert.Equal(version, pending.PeriodicJobVersion);
    }

    [DockerFact]
    public async Task TwoNodesChangingADefinitionAtOnce_LeaveOneLiveRow()
    {
        var host = CreateHost();

        await WithDbAsync(host, db =>
        {
            db.JobEnsurePeriodic<IntegrationJob>(x => x.Run(), "shared", Daily);

            return db.SaveChangesAsync();
        });

        using var scopeA = host.Services.CreateScope();
        using var scopeB = host.Services.CreateScope();

        var nodeA = scopeA.ServiceProvider.GetRequiredService<TestDbContext>();
        var nodeB = scopeB.ServiceProvider.GetRequiredService<TestDbContext>();

        // both read before either saves
        nodeA.JobEnsurePeriodic<IntegrationJob>(x => x.Run(), "shared", "0 0 1 * * *");
        nodeB.JobEnsurePeriodic<IntegrationJob>(x => x.Run(), "shared", "0 0 1 * * *");

        await nodeA.SaveChangesAsync();
        await nodeB.SaveChangesAsync();

        var current = await WithDbAsync(host, db => db.PeriodicJobs.AsNoTracking().Where(p => p.Id == "shared").Select(p => p.Version).SingleAsync());
        var rows = await WithDbAsync(host, db => db.Jobs.AsNoTracking().Where(j => j.PeriodicJobId == "shared").ToListAsync());

        Assert.Equal(3, rows.Count); // the original, and one from each node
        Assert.Single(rows, j => j.PeriodicJobVersion == current);
    }

    [DockerFact]
    public async Task SuccessfulJob_CommitsItsWritesWithTheHistoryRow()
    {
        var host = CreateHost();

        var jobId = await EnqueueAsync(host, x => x.WriteWithoutSavingAsync());

        await host.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 1), "the job to start");

        _state.Release();

        await WaitForAsync(() => WithDbAsync(host, db => db.JobHistory.AnyAsync(h => h.Id == jobId)), "the history row");

        Assert.Equal(1, await CountMarkersAsync(host));
    }

    [DockerFact]
    public async Task JobThatLostItsRow_LosesItsWrites()
    {
        var host = CreateHost();

        var jobId = await EnqueueAsync(host, x => x.WriteWithoutSavingAsync());

        await AssertWritesAreLostWhenTheRowIsGoneAsync(host, jobId);
    }

    [DockerFact]
    public async Task JobThatLostItsRow_LosesWritesSavedInItsOwnOpenTransaction()
    {
        var host = CreateHost();

        var jobId = await EnqueueAsync(host, x => x.SaveInOpenTransactionAsync());

        await AssertWritesAreLostWhenTheRowIsGoneAsync(host, jobId);
    }

    private async Task AssertWritesAreLostWhenTheRowIsGoneAsync(IHost host, long jobId)
    {
        var job = await WithDbAsync(host, db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId));

        await host.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 1), "the job to start");

        // Same effect as the lease expiring and another worker finishing the job first.
        // By primary key, because on SQL Server a scan would wait for the uncommitted row of the job, which waits for this.
        await WithDbAsync(host, db => db.Jobs
            .Where(j => j.Queue == job.Queue && j.ScheduledAt == job.ScheduledAt && j.Id == jobId)
            .ExecuteDeleteAsync());

        _state.Release();

        await WaitForAsync(() => Task.FromResult(_logs.Contains("Job row is already gone")), "the lost row to be logged");

        Assert.Equal(0, await CountMarkersAsync(host));
        Assert.False(await WithDbAsync(host, db => db.JobHistory.AnyAsync(h => h.Id == jobId)));
    }

    private IHost CreateHost(Action<XactJobsOptionsBuilder<TestDbContext>>? configure = null, bool retryOnFailure = false)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);

        builder.Services.AddDbContext<TestDbContext>(options => fixture.Configure(options, retryOnFailure));

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options
                .WithWorkerCount(1)
                .WithPollingInterval(1)
                .WithShutdownTimeout(0)
                .WithPeriodicJobReconciliationInterval(1)
                .WithRetryStrategy(new DefaultRetryStrategy(maxAttempts: 1));

            configure?.Invoke(options);
        });

        builder.Services.AddSingleton(_state);
        builder.Services.AddScoped<IntegrationJob>();

        var host = builder.Build();

        _hosts.Add(host);

        return host;
    }

    private static XactJob NewRow(string queue)
    {
        return new XactJob(0, DateTime.UtcNow.AddMinutes(-1), "Type", "Method", "[]", queue);
    }

    private static XactJob CreateTemplate(TestDbContext db)
    {
        var template = db.JobEnqueue<IntegrationJob>(x => x.Run());

        db.ChangeTracker.Clear();

        return template;
    }

    private static XactJobPeriodic NewPeriodic(XactJob template, string id, bool isActive = true, int version = 1)
    {
        return new XactJobPeriodic(id, DateTime.UtcNow, DateTime.UtcNow, Daily, template.TypeName, template.MethodName, template.MethodArgs, QueueNames.Default, isActive, version);
    }

    private static XactJob NewPeriodicRow(XactJob template, string id, int? version)
    {
        return new XactJob(0, DateTime.UtcNow.AddHours(1), template.TypeName, template.MethodName, template.MethodArgs, QueueNames.Default, id, Daily, version);
    }

    private static Task<long> EnqueueAsync(IHost host, Expression<Func<IntegrationJob, Task>> expression)
    {
        return WithDbAsync(host, async db =>
        {
            var job = db.JobEnqueue(expression);

            await db.SaveChangesAsync();

            return job.Id;
        });
    }

    private async Task<List<XactJob>> ClaimAsync(IHost host, string queue, int maxJobs, Guid leaser, int leaseDurationInSeconds)
    {
        using var scope = host.Services.CreateScope();

        DbContext db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

        return await fixture.Dialect.ClaimJobsAsync(db, queue, maxJobs, leaser, leaseDurationInSeconds, CancellationToken.None);
    }

    private static Task<int> ExecuteAsync(IHost host, (string Sql, object[] Parameters) statement)
    {
        return WithDbAsync(host, db => db.Database.ExecuteSqlRawAsync(statement.Sql, statement.Parameters));
    }

    private static Task<DateTime> GetLeaseAsync(IHost host, long jobId)
    {
        return WithDbAsync(host, db => db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.LeasedUntil!.Value).SingleAsync());
    }

    private static Task<int> CountRowsAsync(IHost host, string periodicJobId)
    {
        return WithDbAsync(host, db => db.Jobs.CountAsync(j => j.PeriodicJobId == periodicJobId));
    }

    private static Task<int> CountMarkersAsync(IHost host)
    {
        return WithDbAsync(host, db => db.Jobs.CountAsync(j => j.Queue == IntegrationJob.MarkerQueue));
    }

    private static async Task<T> WithDbAsync<T>(IHost host, Func<TestDbContext, Task<T>> action)
    {
        using var scope = host.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, string description)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {description}");

            await Task.Delay(20);
        }
    }

    public sealed class IntegrationJobState
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allFailing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _blockedCount;
        private int _failingCount;

        public ConcurrentDictionary<int, int> Runs { get; } = new();

        public int BlockedCount => Volatile.Read(ref _blockedCount);

        public void OnBlocked() => Interlocked.Increment(ref _blockedCount);

        public void Release() => _release.TrySetResult();

        public Task WaitForReleaseAsync() => _release.Task.WaitAsync(WaitTimeout);

        public Task WaitForFailingAsync(int expected)
        {
            if (Interlocked.Increment(ref _failingCount) == expected)
            {
                _allFailing.TrySetResult();
            }

            return _allFailing.Task.WaitAsync(WaitTimeout);
        }
    }

    public sealed class IntegrationJob(TestDbContext db, IntegrationJobState state)
    {
        public const string MarkerQueue = "marker";
        public const string CancelReason = "The job can never succeed";

        public async Task WriteAndCancelAsync()
        {
            // not saved: the runner saves it with the job's row, unless the job fails
            db.JobEnqueue<IntegrationJob>(x => x.Run(), MarkerQueue);

            await Task.Yield();

            throw new XactJobCancelException(CancelReason);
        }

        public void Run()
        {
        }

        public void Count(int number) => state.Runs.AddOrUpdate(number, 1, (_, runs) => runs + 1);

        public Task CountAsync(int number)
        {
            Count(number);

            return Task.CompletedTask;
        }

        public async Task FailTogetherAsync(int expected)
        {
            await state.WaitForFailingAsync(expected);

            throw new InvalidOperationException("Failed on purpose");
        }

        public Task FailWithLongDetailsAsync(int messageLength)
        {
            ThrowFromDepth(40, new string('x', messageLength));

            return Task.CompletedTask;
        }

        private static void ThrowFromDepth(int depth, string message)
        {
            if (depth == 0) throw new InvalidOperationException(message);

            ThrowFromDepth(depth - 1, message);
        }

        public async Task BlockUntilCancelledAsync(CancellationToken cancellationToken)
        {
            state.OnBlocked();

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        public async Task BlockUntilReleasedAsync()
        {
            state.OnBlocked();

            await state.WaitForReleaseAsync();
        }

        public async Task DeletePeriodicAsync(string id, CancellationToken cancellationToken)
        {
            await db.JobDeletePeriodicAsync(id, cancellationToken);

            await db.SaveChangesAsync(cancellationToken);
        }

        public async Task WriteWithoutSavingAsync()
        {
            AddMarker();

            await BlockUntilReleasedAsync();
        }

        public async Task SaveInOpenTransactionAsync()
        {
            await db.Database.BeginTransactionAsync();

            AddMarker();

            await db.SaveChangesAsync();

            await BlockUntilReleasedAsync();
        }

        private void AddMarker()
        {
            db.Jobs.Add(new XactJob(0, DateTime.UtcNow.AddHours(1), "Type", "Method", "[]", MarkerQueue));
        }
    }

    private sealed class FixedRetryStrategy(DateTime retryAt) : IRetryStrategy
    {
        public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount) => retryAt;
    }

    private sealed class LogCapture : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

        public bool HasErrors => _entries.Any(e => e.Level >= LogLevel.Error);

        public bool Contains(string text) => _entries.Any(e => e.Message.Contains(text));

        public override string ToString() => string.Join(Environment.NewLine, _entries.Select(e => $"{e.Level}: {e.Message}"));

        public ILogger CreateLogger(string categoryName) => this;

        public void Dispose()
        {
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                _entries.Enqueue((logLevel, $"{formatter(state, exception)} {exception}"));
            }
        }
    }
}
