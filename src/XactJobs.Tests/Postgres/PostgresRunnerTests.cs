using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using XactJobs.Internal.SqlDialects;

namespace XactJobs.Tests.Postgres;

[Collection(Postgres16Collection.Name)]
public sealed class Postgres16RunnerTests(Postgres16Fixture fixture) : PostgresRunnerTests(fixture);

[Collection(Postgres17Collection.Name)]
public sealed class Postgres17RunnerTests(Postgres17Fixture fixture) : PostgresRunnerTests(fixture);

[Collection(Postgres18Collection.Name)]
public sealed class Postgres18RunnerTests(Postgres18Fixture fixture) : PostgresRunnerTests(fixture);

public abstract class PostgresRunnerTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private readonly PostgresJobState _state = new();
    private readonly PostgreSqlDialect _dialect = new();

    private string _connectionString = "";
    private IHost? _host;

    public async Task InitializeAsync()
    {
        if (PostgresFixture.IsDockerAvailable)
        {
            _connectionString = await fixture.CreateDatabaseAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    [PostgresFact]
    public async Task MigrationScripts_DropLeaserIndex_AndSetFillFactor()
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        // no index on the lease columns: the batch columns and the periodic job are indexed, but a claim doesn't change them
        await using var indexes = new NpgsqlCommand("SELECT string_agg(indexname, ',' ORDER BY indexname) FROM pg_indexes WHERE schemaname = 'xact_jobs' AND tablename = 'job'", connection);
        Assert.Equal("ix_job_awaits_batch_id,ix_job_batch_id,ix_job_periodic_job_id,pk_job", await indexes.ExecuteScalarAsync());

        // only the rows of periodic jobs
        await using var periodicIndex = new NpgsqlCommand("SELECT indexdef FROM pg_indexes WHERE schemaname = 'xact_jobs' AND indexname = 'ix_job_periodic_job_id'", connection);
        Assert.EndsWith("(periodic_job_id) WHERE (periodic_job_id IS NOT NULL)", (string?)await periodicIndex.ExecuteScalarAsync());

        await using var options = new NpgsqlCommand("SELECT reloptions::text FROM pg_class WHERE oid = 'xact_jobs.job'::regclass", connection);
        Assert.Equal("{fillfactor=70}", await options.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task Claim_IsHotUpdate()
    {
        CreateHost();

        await InsertDueJobsAsync(5);

        var claimed = await WithDbAsync(db => _dialect.ClaimJobsAsync(db, QueueNames.Default, 5, Guid.NewGuid(), 60, CancellationToken.None));

        Assert.Equal(5, claimed.Count);

        // closing the backends flushes their statistics
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        long updates = 0, hotUpdates = 0;

        await WaitForAsync(async () =>
        {
            await using var command = new NpgsqlCommand("SELECT n_tup_upd, n_tup_hot_upd FROM pg_stat_user_tables WHERE schemaname = 'xact_jobs' AND relname = 'job'", connection);
            await using var reader = await command.ExecuteReaderAsync();

            await reader.ReadAsync();

            updates = reader.GetInt64(0);
            hotUpdates = reader.GetInt64(1);

            return updates >= 5;
        }, "statistics to be flushed");

        Assert.Equal(5, updates);
        Assert.Equal(5, hotUpdates);
    }

    /// <summary>
    /// The continuation claim also sets the original time, which isn't indexed: it stays a HOT update.
    /// </summary>
    [PostgresFact]
    public async Task ContinuationClaim_IsHotUpdate()
    {
        CreateHost();

        await WithDbAsync(db =>
        {
            for (var i = 0; i < 5; i++)
            {
                // each waits for a batch with no jobs left
                db.Jobs.Add(new XactJob(0, XactJobBase.AwaitingBatchScheduledAt, "Type", "Method", "[]", QueueNames.Default)
                {
                    AwaitsBatchId = Guid.NewGuid(),
                    ContinuationCondition = XactJobContinuation.OnFinish
                });
            }

            return db.SaveChangesAsync();
        });

        var claimedAt = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var claimed = await WithDbAsync(db => _dialect.ClaimContinuationsAsync(db, QueueNames.Default, 5, Guid.NewGuid(), 60, claimedAt, CancellationToken.None));

        Assert.Equal(5, claimed.Count);
        Assert.All(claimed, job => Assert.Equal(claimedAt, job.OriginallyScheduledAt));

        var (updates, hotUpdates) = await GetUpdateStatisticsAsync(5);

        Assert.Equal(5, updates);
        Assert.Equal(5, hotUpdates);
    }

    private async Task<(long Updates, long HotUpdates)> GetUpdateStatisticsAsync(int expectedUpdates)
    {
        // closing the backends flushes their statistics
        NpgsqlConnection.ClearAllPools();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();

        long updates = 0, hotUpdates = 0;

        await WaitForAsync(async () =>
        {
            await using var command = new NpgsqlCommand("SELECT n_tup_upd, n_tup_hot_upd FROM pg_stat_user_tables WHERE schemaname = 'xact_jobs' AND relname = 'job'", connection);
            await using var reader = await command.ExecuteReaderAsync();

            await reader.ReadAsync();

            updates = reader.GetInt64(0);
            hotUpdates = reader.GetInt64(1);

            return updates >= expectedUpdates;
        }, "statistics to be flushed");

        return (updates, hotUpdates);
    }

    [PostgresFact]
    public async Task Claim_WithSpecialCharactersInQueueName_TreatsThemAsData()
    {
        CreateHost();

        const string queueName = "o'brien\"; --";

        await WithDbAsync(db =>
        {
            db.Jobs.Add(new XactJob(0, DateTime.UtcNow.AddMinutes(-1), "Type", "Method", "[]", queueName));
            db.Jobs.Add(new XactJob(0, DateTime.UtcNow.AddMinutes(-1), "Type", "Method", "[]", QueueNames.Default));

            return db.SaveChangesAsync();
        });

        var claimed = await WithDbAsync(db => _dialect.ClaimJobsAsync(db, queueName, 10, Guid.NewGuid(), 60, CancellationToken.None));

        Assert.Equal(queueName, Assert.Single(claimed).Queue);
    }

    [PostgresFact]
    public async Task LeaseSql_OnlyTouchesGivenJobsStillLeasedByTheLeaser()
    {
        CreateHost();

        await InsertDueJobsAsync(3);

        var leaser = Guid.NewGuid();

        var claimed = await WithDbAsync(db => _dialect.ClaimJobsAsync(db, QueueNames.Default, 3, leaser, 60, CancellationToken.None));

        Assert.Equal(3, claimed.Count);

        XactJob[] two = [claimed[0], claimed[1]];

        Assert.Equal(0, await ExecuteAsync(_dialect.GetExtendLeaseSql(Guid.NewGuid(), two, 3600)));
        Assert.Equal(2, await ExecuteAsync(_dialect.GetExtendLeaseSql(leaser, two, 3600)));

        var leases = await WithDbAsync(db => db.Jobs.AsNoTracking().ToDictionaryAsync(j => j.Id, j => j.LeasedUntil!.Value));
        var cutoff = DateTime.UtcNow.AddMinutes(30);

        Assert.True(leases[claimed[0].Id] > cutoff);
        Assert.True(leases[claimed[1].Id] > cutoff);
        Assert.True(leases[claimed[2].Id] < cutoff);

        Assert.Equal(0, await ExecuteAsync(_dialect.GetClearLeaseSql(Guid.NewGuid(), two)));
        Assert.Equal(2, await ExecuteAsync(_dialect.GetClearLeaseSql(leaser, two)));

        Assert.Equal(2, await WithDbAsync(db => db.Jobs.CountAsync(j => j.Leaser == null)));
    }

    [PostgresFact]
    public async Task ParallelFailures_EachGetErrorCountHistoryAndReschedule()
    {
        const int jobCount = 4;

        var retryAt = DateTime.UtcNow.AddHours(1);

        CreateHost(options =>
        {
            options.WithWorkerCount(1).WithRetryStrategy(new FixedRetryStrategy(retryAt));
            options.Options.MaxDegreeOfParallelism = jobCount;
        });

        var jobIds = await WithDbAsync(async db =>
        {
            var jobs = Enumerable.Range(0, jobCount)
                .Select(_ => db.JobEnqueue<PostgresJob>(x => x.FailTogetherAsync(jobCount)))
                .ToList();

            await db.SaveChangesAsync();

            return jobs.Select(j => j.Id).Order().ToList();
        });

        await _host!.StartAsync();

        await WaitForAsync(() => WithDbAsync(async db => await db.JobHistory.CountAsync() >= jobCount), "history rows");

        var history = await WithDbAsync(db => db.JobHistory.AsNoTracking().ToListAsync());

        Assert.Equal(jobIds, history.Select(h => h.Id).Order());
        Assert.All(history, h =>
        {
            Assert.Equal(XactJobStatus.Failed, h.Status);
            Assert.Equal(1, h.ErrorCount);
        });

        var pending = await WithDbAsync(db => db.Jobs.AsNoTracking().Where(j => j.PeriodicJobId == null).ToListAsync());

        Assert.Equal(jobCount, pending.Count);
        Assert.All(pending, j =>
        {
            Assert.Equal(1, j.ErrorCount);
            Assert.Equal(retryAt, j.ScheduledAt, TimeSpan.FromSeconds(1));
        });
    }

    [PostgresFact]
    public async Task RunningJob_KeepsItsLeaseExtended_AndNoOtherWorkerClaimsIt()
    {
        const int leaseDurationInSeconds = 2;

        CreateHost(options => options.WithWorkerCount(2).WithShutdownTimeout(0).WithLeaseDuration(leaseDurationInSeconds));

        var jobId = await WithDbAsync(async db =>
        {
            var job = db.JobEnqueue<PostgresJob>(x => x.BlockUntilCancelledAsync(CancellationToken.None));

            await db.SaveChangesAsync();

            return job.Id;
        });

        await _host!.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 1), "the job to start");

        var firstLease = await WithDbAsync(db => db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.LeasedUntil).SingleAsync());

        await Task.Delay(TimeSpan.FromSeconds(leaseDurationInSeconds + 2));

        var laterLease = await WithDbAsync(db => db.Jobs.AsNoTracking().Where(j => j.Id == jobId).Select(j => j.LeasedUntil).SingleAsync());

        Assert.True(laterLease > firstLease, $"Lease was not extended: {firstLease:O} -> {laterLease:O}");
        Assert.Equal(1, _state.BlockedCount);
    }

    [PostgresFact]
    public async Task Stop_ClearsLeasesOfRunningJobs_ButNotOfOtherLeasers()
    {
        CreateHost(options =>
        {
            options.WithWorkerCount(1).WithShutdownTimeout(0);
            options.Options.MaxDegreeOfParallelism = 2;
        });

        var (jobIds, otherJobId) = await WithDbAsync(async db =>
        {
            var blocking = Enumerable.Range(0, 2)
                .Select(_ => db.JobEnqueue<PostgresJob>(x => x.BlockUntilCancelledAsync(CancellationToken.None)))
                .ToList();

            var other = new XactJob(0, DateTime.UtcNow.AddMinutes(-1), "Type", "Method", "[]", QueueNames.Default,
                                    leaser: Guid.NewGuid(), leasedUntil: DateTime.UtcNow.AddHours(1));

            db.Jobs.Add(other);

            await db.SaveChangesAsync();

            return (blocking.Select(j => j.Id).ToList(), other.Id);
        });

        await _host!.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 2), "the jobs to start");

        Assert.All(await GetJobsAsync(jobIds), j => Assert.NotNull(j.Leaser));

        await _host.StopAsync();

        Assert.All(await GetJobsAsync(jobIds), j =>
        {
            Assert.Null(j.Leaser);
            Assert.Null(j.LeasedUntil);
            Assert.Equal(0, j.ErrorCount);
        });

        Assert.NotNull((await GetJobsAsync([otherJobId])).Single().Leaser);
    }

    [PostgresFact]
    public async Task SlowJob_DoesNotStopShortJobsOnTheSameWorker()
    {
        const int shortJobs = 20;

        CreateHost(options =>
        {
            options.WithWorkerCount(1).WithShutdownTimeout(0);
            options.Options.MaxDegreeOfParallelism = 2;
        });

        await WithDbAsync(db =>
        {
            db.JobEnqueue<PostgresJob>(x => x.BlockUntilCancelledAsync(CancellationToken.None));

            return db.SaveChangesAsync();
        });

        await _host!.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 1), "the slow job to start");

        // added now, so that they can only run if the worker keeps polling while the slow job is running
        await WithDbAsync(db =>
        {
            for (var i = 0; i < shortJobs; i++)
            {
                var number = i;

                db.JobEnqueue<PostgresJob>(x => x.Count(number));
            }

            return db.SaveChangesAsync();
        });

        await WaitForAsync(() => Task.FromResult(_state.Runs.Count == shortJobs), "the short jobs to run");

        // their rows are removed when the worker commits, right after they run
        await WaitForAsync(() => WithDbAsync(async db => await db.Jobs.CountAsync(j => j.PeriodicJobId == null) == 1), "the short jobs to be committed");

        // only the slow one is left, still leased
        var left = await WithDbAsync(db => db.Jobs.AsNoTracking().Where(j => j.PeriodicJobId == null).SingleAsync());

        Assert.NotNull(left.Leaser);
    }

    [PostgresFact]
    public async Task Stop_AfterTheTimeout_ClearsLeasesOnlyOfUnfinishedJobs()
    {
        CreateHost(options =>
        {
            options.WithWorkerCount(1).WithShutdownTimeout(1);
            options.Options.MaxDegreeOfParallelism = 2;
        });

        var (blockedId, finishedId) = await WithDbAsync(async db =>
        {
            var blocked = db.JobScheduleAt<PostgresJob>(x => x.BlockUntilCancelledAsync(CancellationToken.None), DateTime.UtcNow.AddHours(-1));
            var finished = db.JobEnqueue<PostgresJob>(x => x.Count(1));

            await db.SaveChangesAsync();

            return (blocked.Id, finished.Id);
        });

        await _host!.StartAsync();

        await WaitForAsync(() => WithDbAsync(db => db.JobHistory.AnyAsync(h => h.Id == finishedId)), "the short job to finish");
        await WaitForAsync(() => Task.FromResult(_state.BlockedCount == 1), "the slow job to start");

        await _host.StopAsync();

        var blocked = (await GetJobsAsync([blockedId])).Single();

        Assert.Null(blocked.Leaser);
        Assert.Equal(0, blocked.ErrorCount);
        Assert.Empty(await GetJobsAsync([finishedId]));
    }

    [PostgresFact]
    public async Task CompetingWorkers_RunEveryJobExactlyOnce()
    {
        const int jobCount = 200;

        CreateHost(options =>
        {
            options.WithWorkerCount(4).WithMaxDegreeOfParallelism(4);
        });

        await WithDbAsync(async db =>
        {
            for (var i = 0; i < jobCount; i++)
            {
                var number = i;

                db.JobEnqueue<PostgresJob>(x => x.Count(number));
            }

            return await db.SaveChangesAsync();
        });

        await _host!.StartAsync();

        await WaitForAsync(() => WithDbAsync(async db => !await db.Jobs.AnyAsync(j => j.PeriodicJobId == null)), "all jobs to finish");

        Assert.Equal(jobCount, _state.Runs.Count);
        Assert.All(_state.Runs.Values, runs => Assert.Equal(1, runs));
    }

    private void CreateHost(Action<XactJobsOptionsBuilder<TestDbContext>>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<TestDbContext>(options => options.UseNpgsql(_connectionString));

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options.WithPollingInterval(1);

            configure?.Invoke(options);
        });

        builder.Services.AddSingleton(_state);
        builder.Services.AddScoped<PostgresJob>();

        _host = builder.Build();
    }

    private Task InsertDueJobsAsync(int count)
    {
        return WithDbAsync(db =>
        {
            for (var i = 0; i < count; i++)
            {
                db.Jobs.Add(new XactJob(0, DateTime.UtcNow.AddMinutes(-1), "Type", "Method", "[]", QueueNames.Default));
            }

            return db.SaveChangesAsync();
        });
    }

    private Task<int> ExecuteAsync((string Sql, object[] Parameters) statement)
    {
        return WithDbAsync(db => db.Database.ExecuteSqlRawAsync(statement.Sql, statement.Parameters));
    }

    private Task<List<XactJob>> GetJobsAsync(List<long> ids)
    {
        return WithDbAsync(db => db.Jobs.AsNoTracking().Where(j => ids.Contains(j.Id)).ToListAsync());
    }

    private async Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        using var scope = _host!.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
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

    public sealed class PostgresJobState
    {
        private readonly TaskCompletionSource _allFailing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _failingCount;
        private int _blockedCount;

        public ConcurrentDictionary<int, int> Runs { get; } = new();

        public int BlockedCount => Volatile.Read(ref _blockedCount);

        public void OnBlocked() => Interlocked.Increment(ref _blockedCount);

        public Task WaitForFailingAsync(int expected)
        {
            if (Interlocked.Increment(ref _failingCount) == expected)
            {
                _allFailing.TrySetResult();
            }

            return _allFailing.Task.WaitAsync(WaitTimeout);
        }
    }

    public sealed class PostgresJob(PostgresJobState state)
    {
        public void Count(int number) => state.Runs.AddOrUpdate(number, 1, (_, runs) => runs + 1);

        public async Task FailTogetherAsync(int expected)
        {
            await state.WaitForFailingAsync(expected);

            throw new InvalidOperationException("Failed on purpose");
        }

        public async Task BlockUntilCancelledAsync(CancellationToken cancellationToken)
        {
            state.OnBlocked();

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    private sealed class FixedRetryStrategy(DateTime retryAt) : IRetryStrategy
    {
        public DateTime? GetRetryTimeUtc(XactJob job, int newErrorCount) => retryAt;
    }
}
