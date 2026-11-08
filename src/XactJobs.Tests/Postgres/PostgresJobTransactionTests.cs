using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests.Postgres;

/// <summary>
/// What the README promises about the database writes of a job using the scoped DbContext.
/// </summary>
[Collection(Postgres16Collection.Name)]
public sealed class Postgres16JobTransactionTests(Postgres16Fixture fixture) : PostgresJobTransactionTests(fixture);

[Collection(Postgres17Collection.Name)]
public sealed class Postgres17JobTransactionTests(Postgres17Fixture fixture) : PostgresJobTransactionTests(fixture);

[Collection(Postgres18Collection.Name)]
public sealed class Postgres18JobTransactionTests(Postgres18Fixture fixture) : PostgresJobTransactionTests(fixture);

public abstract class PostgresJobTransactionTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string MarkerQueue = "marker";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private readonly TransactionJobState _state = new();

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
        _state.Release();

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    [PostgresFact]
    public async Task SuccessfulJob_CommitsItsWritesWithTheHistoryRow()
    {
        var jobId = await StartJobAsync(x => x.WriteWithoutSavingAsync());

        _state.Release();

        await WaitForAsync(() => WithDbAsync(db => db.JobHistory.AnyAsync(h => h.Id == jobId)), "the history row");

        Assert.Equal(1, await CountMarkersAsync());
        Assert.False(await WithDbAsync(db => db.Jobs.AnyAsync(j => j.Id == jobId)));
    }

    [PostgresFact]
    public async Task FailingJob_LosesItsWrites()
    {
        var jobId = await StartJobAsync(x => x.WriteAndFailAsync());

        _state.Release();

        await WaitForAsync(() => WithDbAsync(db => db.JobHistory.AnyAsync(h => h.Id == jobId && h.Status == XactJobStatus.Cancelled)), "the failure to be recorded");

        Assert.Equal(0, await CountMarkersAsync());
    }

    [PostgresFact]
    public async Task JobThatLostItsRow_LosesItsWrites()
    {
        var jobId = await StartJobAsync(x => x.WriteWithoutSavingAsync());

        // same effect as the lease expiring and another worker finishing the job first
        await WithDbAsync(db => db.Jobs.Where(j => j.Id == jobId).ExecuteDeleteAsync());

        _state.Release();

        await WaitForAsync(() => Task.FromResult(_state.Finished), "the job to finish");

        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.Equal(0, await CountMarkersAsync());
        Assert.False(await WithDbAsync(db => db.JobHistory.AnyAsync(h => h.Id == jobId)));
    }

    [PostgresFact]
    public async Task JobThatLostItsRow_LosesWritesSavedInsideItsOwnOpenTransaction()
    {
        var jobId = await StartJobAsync(x => x.SaveInOpenTransactionAsync());

        await WithDbAsync(db => db.Jobs.Where(j => j.Id == jobId).ExecuteDeleteAsync());

        _state.Release();

        await WaitForAsync(() => Task.FromResult(_state.Finished), "the job to finish");

        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.Equal(0, await CountMarkersAsync());
    }

    [PostgresFact]
    public async Task SuccessfulJob_HasItsOpenTransactionCommittedByTheWorker()
    {
        var jobId = await StartJobAsync(x => x.SaveInOpenTransactionAsync());

        _state.Release();

        await WaitForAsync(() => WithDbAsync(db => db.JobHistory.AnyAsync(h => h.Id == jobId)), "the history row");

        Assert.Equal(1, await CountMarkersAsync());
    }

    [PostgresFact]
    public async Task JobThatLostItsRow_KeepsWritesItSavedWithoutATransaction()
    {
        var jobId = await StartJobAsync(x => x.SaveWithoutTransactionAsync());

        await WithDbAsync(db => db.Jobs.Where(j => j.Id == jobId).ExecuteDeleteAsync());

        _state.Release();

        await WaitForAsync(() => Task.FromResult(_state.Finished), "the job to finish");

        await Task.Delay(TimeSpan.FromSeconds(1));

        // this is why the README says not to save without a transaction
        Assert.Equal(1, await CountMarkersAsync());
    }

    private async Task<long> StartJobAsync(System.Linq.Expressions.Expression<Func<TransactionJob, Task>> expression)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<TestDbContext>(options => options.UseNpgsql(_connectionString));

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options
                .WithWorkerCount(1)
                .WithPollingInterval(1)
                .WithShutdownTimeout(0)
                .WithRetryStrategy(new DefaultRetryStrategy(maxAttempts: 1));
        });

        builder.Services.AddSingleton(_state);
        builder.Services.AddScoped<TransactionJob>();

        _host = builder.Build();

        var jobId = await WithDbAsync(async db =>
        {
            var job = db.JobEnqueue(expression);

            await db.SaveChangesAsync();

            return job.Id;
        });

        await _host.StartAsync();

        await WaitForAsync(() => Task.FromResult(_state.Started), "the job to start");

        return jobId;
    }

    private Task<int> CountMarkersAsync()
    {
        return WithDbAsync(db => db.Jobs.CountAsync(j => j.Queue == MarkerQueue));
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

            await Task.Delay(20);
        }
    }

    public sealed class TransactionJobState
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _started;
        private volatile bool _finished;

        public bool Started => _started;
        public bool Finished => _finished;

        public void Release() => _release.TrySetResult();

        public async Task WaitAsync()
        {
            _started = true;

            await _release.Task.WaitAsync(WaitTimeout);

            _finished = true;
        }
    }

    /// <summary>
    /// A row in a queue no worker polls stands for the business data of the job.
    /// </summary>
    public sealed class TransactionJob(TestDbContext db, TransactionJobState state)
    {
        public async Task WriteWithoutSavingAsync()
        {
            AddMarker();

            await state.WaitAsync();
        }

        public async Task WriteAndFailAsync()
        {
            AddMarker();

            await state.WaitAsync();

            throw new InvalidOperationException("Failed on purpose");
        }

        public async Task SaveInOpenTransactionAsync()
        {
            await db.Database.BeginTransactionAsync();

            AddMarker();

            await db.SaveChangesAsync();

            await state.WaitAsync();
        }

        public async Task SaveWithoutTransactionAsync()
        {
            AddMarker();

            await db.SaveChangesAsync();

            await state.WaitAsync();
        }

        private void AddMarker()
        {
            db.Jobs.Add(new XactJob(0, DateTime.UtcNow.AddHours(1), "Type", "Method", "[]", MarkerQueue));
        }
    }
}
