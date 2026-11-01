using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XactJobs.Internal.SqlDialects;

namespace XactJobs.Tests;

public class SqliteLeaseTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private readonly BlockingJobState _state = new();
    private readonly SqliteDialect _dialect = new();

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private IHost? _host;

    public SqliteLeaseTests()
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

    [Fact]
    public async Task ExtendLeaseSql_OnlyTouchesGivenJobsStillLeasedByTheLeaser()
    {
        await CreateHostAsync();

        var leaser = Guid.NewGuid();
        var otherLeaser = Guid.NewGuid();
        var originalLease = DateTime.UtcNow.AddSeconds(5);

        var (target, notGiven, takenOver) = await InsertLeasedJobsAsync(leaser, otherLeaser, originalLease);

        var (sql, parameters) = _dialect.GetExtendLeaseSql(leaser, [target, takenOver], 3600);

        var updated = await ExecuteAsync(sql, parameters);

        Assert.Equal(1, updated);

        Assert.True((await GetJobAsync(target.Id)).LeasedUntil > DateTime.UtcNow.AddMinutes(30));
        Assert.Equal(originalLease, (await GetJobAsync(notGiven.Id)).LeasedUntil!.Value, TimeSpan.FromSeconds(1));
        Assert.Equal(originalLease, (await GetJobAsync(takenOver.Id)).LeasedUntil!.Value, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ClearLeaseSql_OnlyTouchesGivenJobsStillLeasedByTheLeaser()
    {
        await CreateHostAsync();

        var leaser = Guid.NewGuid();
        var otherLeaser = Guid.NewGuid();

        var (target, notGiven, takenOver) = await InsertLeasedJobsAsync(leaser, otherLeaser, DateTime.UtcNow.AddSeconds(5));

        var (sql, parameters) = _dialect.GetClearLeaseSql(leaser, [target, takenOver]);

        var updated = await ExecuteAsync(sql, parameters);

        Assert.Equal(1, updated);

        var cleared = await GetJobAsync(target.Id);

        Assert.Null(cleared.Leaser);
        Assert.Null(cleared.LeasedUntil);
        Assert.Equal(leaser, (await GetJobAsync(notGiven.Id)).Leaser);
        Assert.Equal(otherLeaser, (await GetJobAsync(takenOver.Id)).Leaser);
    }

    [Fact]
    public async Task Claim_WithSpecialCharactersInQueueName_TreatsThemAsData()
    {
        await CreateHostAsync();

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

    [Fact]
    public async Task ClaimAndExtend_KeepFractionsOfASecondInTheLease()
    {
        await CreateHostAsync();

        await WithDbAsync(db =>
        {
            db.Jobs.Add(new XactJob(0, DateTime.UtcNow.AddMinutes(-1), "Type", "Method", "[]", QueueNames.Default));

            return db.SaveChangesAsync();
        });

        var leaser = Guid.NewGuid();

        var claimed = await WithDbAsync(db => _dialect.ClaimJobsAsync(db, QueueNames.Default, 1, leaser, 60, CancellationToken.None));

        // datetime('subsec') always writes 3 decimals, while the whole second version writes none
        Assert.Matches(@"\.\d{3}$", await GetRawLeasedUntilAsync());

        var (sql, parameters) = _dialect.GetExtendLeaseSql(leaser, claimed, 60);

        Assert.Equal(1, await ExecuteAsync(sql, parameters));
        Assert.Matches(@"\.\d{3}$", await GetRawLeasedUntilAsync());
    }

    [Fact]
    public async Task RunningJob_KeepsItsLeaseExtended_AndNoOtherWorkerClaimsIt()
    {
        const int leaseDurationInSeconds = 3;

        await CreateHostAsync(options =>
        {
            options
                .WithWorkerCount(2)
                .WithLeaseDuration(leaseDurationInSeconds);
        });

        var jobId = await EnqueueBlockingJobsAsync(1);

        await _host!.StartAsync();

        await _state.WaitForStartedAsync(1).WaitAsync(WaitTimeout);

        var firstLease = (await GetJobAsync(jobId[0])).LeasedUntil!.Value;

        // outlive the original lease, by a margin
        await Task.Delay(TimeSpan.FromSeconds(leaseDurationInSeconds + 2));

        var job = await GetJobAsync(jobId[0]);

        Assert.True(job.LeasedUntil > firstLease, $"Lease was not extended: {firstLease:O} -> {job.LeasedUntil:O}");
        Assert.Equal(1, _state.StartedCount);
    }

    [Fact]
    public async Task Stop_ClearsLeasesOfRunningAndNotStartedJobs_ButNotOfOtherLeasers()
    {
        await CreateHostAsync(options =>
        {
            options.WithWorkerCount(1);
            options.Options.MaxDegreeOfParallelism = 1;
        });

        var jobIds = await EnqueueBlockingJobsAsync(2);

        var otherLeaser = Guid.NewGuid();
        var otherLease = DateTime.UtcNow.AddHours(1);
        var otherJob = await InsertJobAsync(otherLeaser, otherLease);

        await _host!.StartAsync();

        await _state.WaitForStartedAsync(1).WaitAsync(WaitTimeout);

        // both claimed, only one of them running
        Assert.All(await GetJobsAsync(jobIds), j => Assert.NotNull(j.Leaser));
        Assert.Equal(1, _state.StartedCount);

        await _host.StopAsync();

        Assert.All(await GetJobsAsync(jobIds), j =>
        {
            Assert.Null(j.Leaser);
            Assert.Null(j.LeasedUntil);
            Assert.Equal(0, j.ErrorCount);
        });

        var untouched = await GetJobAsync(otherJob.Id);

        Assert.Equal(otherLeaser, untouched.Leaser);
        Assert.Equal(otherLease, untouched.LeasedUntil!.Value, TimeSpan.FromSeconds(1));

        Assert.Empty(await WithDbAsync(db => db.JobHistory.ToListAsync()));
    }

    private async Task CreateHostAsync(Action<XactJobsOptionsBuilder<TestDbContext>>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString));

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options.WithPollingInterval(1);

            configure?.Invoke(options);
        });

        builder.Services.AddSingleton(_state);
        builder.Services.AddScoped<BlockingJob>();

        _host = builder.Build();

        await WithDbAsync(db => db.Database.EnsureCreatedAsync());
    }

    private async Task<List<long>> EnqueueBlockingJobsAsync(int count)
    {
        return await WithDbAsync(async db =>
        {
            var jobs = Enumerable.Range(0, count)
                .Select(_ => db.JobEnqueue<BlockingJob>(x => x.BlockUntilCancelledAsync(CancellationToken.None)))
                .ToList();

            await db.SaveChangesAsync();

            return jobs.Select(j => j.Id).ToList();
        });
    }

    private async Task<(XactJob Target, XactJob NotGiven, XactJob TakenOver)> InsertLeasedJobsAsync(Guid leaser, Guid otherLeaser, DateTime leasedUntil)
    {
        return (await InsertJobAsync(leaser, leasedUntil),
                await InsertJobAsync(leaser, leasedUntil),
                await InsertJobAsync(otherLeaser, leasedUntil));
    }

    /// <summary>
    /// The leaser is written the same way the claim writes it, in lowercase.
    /// </summary>
    private Task<XactJob> InsertJobAsync(Guid leaser, DateTime leasedUntil)
    {
        return WithDbAsync(async db =>
        {
            var job = new XactJob(0, DateTime.UtcNow.AddMinutes(-1), "Type", "Method", "[]", QueueNames.Default, leasedUntil: leasedUntil);

            db.Jobs.Add(job);

            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlRawAsync("UPDATE xact_jobs_job SET leaser = {0} WHERE id = {1}", leaser.ToString(), job.Id);

            return job;
        });
    }

    private Task<string> GetRawLeasedUntilAsync()
    {
        return WithDbAsync(async db =>
        {
            await db.Database.OpenConnectionAsync();

            await using var command = db.Database.GetDbConnection().CreateCommand();

            command.CommandText = "SELECT leased_until FROM xact_jobs_job WHERE leased_until IS NOT NULL";

            return (string)(await command.ExecuteScalarAsync())!;
        });
    }

    private Task<int> ExecuteAsync(string sql, object[] parameters)
    {
        return WithDbAsync(db => db.Database.ExecuteSqlRawAsync(sql, parameters));
    }

    private Task<XactJob> GetJobAsync(long id)
    {
        return WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == id));
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

    public sealed class BlockingJobState
    {
        private int _startedCount;

        public int StartedCount => Volatile.Read(ref _startedCount);

        public void OnStarted() => Interlocked.Increment(ref _startedCount);

        public async Task WaitForStartedAsync(int count)
        {
            while (StartedCount < count)
            {
                await Task.Delay(20);
            }
        }
    }

    public sealed class BlockingJob(BlockingJobState state)
    {
        public async Task BlockUntilCancelledAsync(CancellationToken cancellationToken)
        {
            state.OnStarted();

            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }
}
