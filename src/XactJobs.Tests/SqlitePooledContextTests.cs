using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

/// <summary>
/// An app can register its context with AddDbContextPool: the workers then reuse pooled contexts, and must give them back as they
/// got them. The runner changes two things on a job's context: AutoTransactionBehavior (Always), and the save check's handler, while
/// the job runs.
/// </summary>
public sealed class SqlitePooledContextTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";
    private readonly SqliteConnection _memoryConnection;
    private readonly WarningCollector _warnings = new();

    private IHost? _host;

    public SqlitePooledContextTests()
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
    public async Task Jobs_RunWithPooledContexts_AndGiveThemBackAsTheyWere()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_warnings);

        builder.Services.AddDbContextPool<TestDbContext>(options => options.UseSqlite(_connectionString));
        builder.Services.AddXactJobs<TestDbContext>(options => options.WithPollingInterval(1).WithWorkerCount(1).WithMaxDegreeOfParallelism(1));

        _host = builder.Build();

        await WithDbAsync(db => db.Database.EnsureCreatedAsync());

        await WithDbAsync(db =>
        {
            for (var i = 0; i < 10; i++)
            {
                db.JobEnqueue(() => PooledContextJobs.Run());
            }

            return db.SaveChangesAsync();
        });

        await _host.StartAsync();

        var deadline = DateTime.UtcNow + WaitTimeout;

        while (await WithDbAsync(db => db.JobHistory.CountAsync()) < 10)
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the jobs");
            await Task.Delay(50);
        }

        Assert.Equal(10, await WithDbAsync(db => db.JobHistory.CountAsync(h => h.Status == XactJobStatus.Completed)));

        await _host.StopAsync();

        // the contexts the workers used are back in the pool: take several at once, so the ones they used are among them
        var scopes = Enumerable.Range(0, 8).Select(_ => _host.Services.CreateScope()).ToList();

        try
        {
            foreach (var scope in scopes)
            {
                var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

                Assert.Equal(AutoTransactionBehavior.WhenNeeded, db.Database.AutoTransactionBehavior);

                // a save outside a transaction, as an app may do: a save check's handler left on the context would warn about the job
                db.JobEnqueue(() => PooledContextJobs.Run());
                await db.SaveChangesAsync();
            }
        }
        finally
        {
            foreach (var scope in scopes) scope.Dispose();
        }

        Assert.DoesNotContain(_warnings.Messages, m => m.Contains("outside a transaction"));
    }

    private async Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        using var scope = _host!.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    private sealed class WarningCollector : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning) Messages.Enqueue(formatter(state, exception));
        }

        public void Dispose()
        {
        }
    }
}

public static class PooledContextJobs
{
    public static void Run()
    {
    }
}
