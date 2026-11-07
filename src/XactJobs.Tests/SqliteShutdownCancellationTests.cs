using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

/// <summary>
/// A command cancelled by the shutdown doesn't always throw an OperationCanceledException: SqlClient throws a SqlException
/// ("Operation cancelled by user"). Here a job and an interceptor throw like that once their token is cancelled, and the shutdown
/// must neither log an error nor count a failed attempt.
/// </summary>
public class SqliteShutdownCancellationTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private readonly CapturingLoggerProvider _logs = new();
    private readonly ProviderLikeCancellation _cancellation = new();

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private IHost? _host;

    public SqliteShutdownCancellationTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
    }

    public Task InitializeAsync() => _memoryConnection.OpenAsync();

    public async Task DisposeAsync()
    {
        _host?.Dispose();

        await _memoryConnection.DisposeAsync();
    }

    [Fact]
    public async Task JobCancelledByShutdown_ThrowingAProviderException_RunsAgainWithoutAFailedAttempt()
    {
        await CreateHostAsync(options => options.WithWorkerCount(1).WithShutdownTimeout(0));

        var jobId = await WithDbAsync(async db =>
        {
            var job = db.JobEnqueue<ProviderLikeJob>(x => x.ThrowWhenCancelledAsync(CancellationToken.None));

            await db.SaveChangesAsync();

            return job.Id;
        });

        await _host!.StartAsync();

        await _cancellation.Entered.WaitAsync(WaitTimeout);

        await _host.StopAsync();

        Assert.True(_cancellation.Threw);
        Assert.Empty(_logs.Errors);

        // not a failed attempt: that would be in the history, with a retry in place of the job
        Assert.Empty(await WithDbAsync(db => db.JobHistory.ToListAsync()));

        var job = await WithDbAsync(db => db.Jobs.AsNoTracking().SingleAsync(j => j.Id == jobId));

        Assert.Equal(0, job.ErrorCount);
        Assert.Null(job.Leaser);
    }

    [Fact]
    public async Task ClaimCancelledByShutdown_ThrowingAProviderException_IsNotLoggedAsAnError()
    {
        await CreateHostAsync(options => options.WithWorkerCount(1));

        // the claims: of the continuations, and of the due jobs
        _cancellation.Matches = sql => sql.Contains("RETURNING");

        await _host!.StartAsync();

        await _cancellation.Entered.WaitAsync(WaitTimeout);

        await _host.StopAsync();

        Assert.True(_cancellation.Threw);
        Assert.Empty(_logs.Errors);
    }

    [Fact]
    public async Task LeaseExtensionCancelledByShutdown_ThrowingAProviderException_IsNotLoggedAsAnError()
    {
        await CreateHostAsync(options => options.WithWorkerCount(1).WithShutdownTimeout(0).WithLeaseDuration(4));

        // extended every 3 seconds, and the test stops right away, before the lease expires and the job could be claimed again
        _cancellation.Matches = sql => sql.Contains("SET leased_until");

        await WithDbAsync(async db =>
        {
            db.JobEnqueue<ProviderLikeJob>(x => x.WaitUntilCancelledAsync(CancellationToken.None));

            return await db.SaveChangesAsync();
        });

        await _host!.StartAsync();

        await _cancellation.Entered.WaitAsync(WaitTimeout);

        await _host.StopAsync();

        Assert.True(_cancellation.Threw);
        Assert.Empty(_logs.Errors);
    }

    [Fact]
    public async Task SchedulerCycleCancelledByShutdown_ThrowingAProviderException_IsNotLoggedAsAnError()
    {
        await CreateHostAsync(options => options.WithWorkerCount(1));

        _cancellation.Matches = sql => sql.Contains("xact_jobs_job_periodic");

        await _host!.StartAsync();

        await _cancellation.Entered.WaitAsync(WaitTimeout);

        await _host.StopAsync();

        Assert.True(_cancellation.Threw);
        Assert.Empty(_logs.Errors);
    }

    private async Task CreateHostAsync(Action<XactJobsOptionsBuilder<TestDbContext>> configure)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_logs);

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString).AddInterceptors(_cancellation));

        builder.Services.AddXactJobs<TestDbContext>(options =>
        {
            options.WithPollingInterval(1);

            configure(options);
        });

        builder.Services.AddSingleton(_cancellation);
        builder.Services.AddScoped<ProviderLikeJob>();

        _host = builder.Build();

        await WithDbAsync(db => db.Database.EnsureCreatedAsync());
    }

    private async Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        using var scope = _host!.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    /// <summary>
    /// Waits for the cancellation, then throws like SqlClient: not an OperationCanceledException. Once, in the first command that
    /// <see cref="Matches"/> (set after the schema is created), and in <see cref="ProviderLikeJob.ThrowWhenCancelledAsync"/>.
    /// </summary>
    public sealed class ProviderLikeCancellation : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _intercepted;
        private volatile bool _threw;

        public Func<string, bool> Matches { get; set; } = _ => false;

        public Task Entered => _entered.Task;

        public bool Threw => _threw;

        public async Task WaitForCancellationAndThrowAsync(CancellationToken cancellationToken)
        {
            _entered.TrySetResult();

            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _threw = true;

                throw new InvalidOperationException("Operation cancelled by user (like SqlClient's SqlException)");
            }
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            await InterceptAsync(command, cancellationToken);

            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            await InterceptAsync(command, cancellationToken);

            return result;
        }

        public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            await InterceptAsync(command, cancellationToken);

            return result;
        }

        private async Task InterceptAsync(DbCommand command, CancellationToken cancellationToken)
        {
            if (!Matches(command.CommandText) || Interlocked.Exchange(ref _intercepted, 1) == 1) return;

            await WaitForCancellationAndThrowAsync(cancellationToken);
        }
    }

    public sealed class ProviderLikeJob(ProviderLikeCancellation cancellation)
    {
        public Task ThrowWhenCancelledAsync(CancellationToken cancellationToken) => cancellation.WaitForCancellationAndThrowAsync(cancellationToken);

        public Task WaitUntilCancelledAsync(CancellationToken cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken);
    }

    /// <summary>
    /// XactJobs' errors only: EF logs its own for a failed command, also when it's cancelled.
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _errors = new();

        public IReadOnlyCollection<string> Errors => _errors;

        public ILogger CreateLogger(string categoryName) =>
            categoryName.StartsWith("XactJobs.", StringComparison.Ordinal) ? new CapturingLogger(_errors) : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> errors) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Error) errors.Enqueue(formatter(state, exception) + " " + exception?.Message);
            }
        }
    }
}
