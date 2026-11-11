using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XactJobs.Internal;
using XactJobs.Tests.PeriodicJobs;

namespace XactJobs.Tests;

/// <summary>
/// Periodic jobs from <see cref="XactPeriodicJobAttribute"/>: the scan of <c>XactJobs.Tests.PeriodicJobs</c>, which has only valid
/// ones, and the checks, on the invalid methods below (never in an assembly scan: this assembly isn't scanned).
/// </summary>
public class PeriodicJobAttributeTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    public PeriodicJobAttributeTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
    }

    public Task InitializeAsync() => _memoryConnection.OpenAsync();

    public async Task DisposeAsync() => await _memoryConnection.DisposeAsync();

    [Fact]
    public void Scan_AddsTheJobs_ToTheirQueues()
    {
        var options = AddXactJobs(o => o
            .WithIsolatedQueue(AttributeJobs.IsolatedQueue)
            .WithPeriodicJobsFromAssemblyOf<AttributeJobs>());

        Assert.Equal(["attr_a", "attr_b", "attr_static"], options.PeriodicJobs.Keys.Order());
        Assert.Equal(["attr_isolated"], options.IsolatedQueues[AttributeJobs.IsolatedQueue].PeriodicJobs.Keys);

        Assert.Equal(CronExpressions.EverySecond, options.PeriodicJobs["attr_a"].CronExpression);
        Assert.Equal(CronExpressions.Daily, options.PeriodicJobs["attr_static"].CronExpression);
    }

    [Fact]
    public void Scan_StoresTheSameDefinition_AsWithPeriodicJob()
    {
        var options = AddXactJobs(o => o
            .WithIsolatedQueue(AttributeJobs.IsolatedQueue)
            .WithPeriodicJobsFromAssembly(typeof(AttributeJobs).Assembly));

        AssertSameDefinition(options, "attr_a", (System.Linq.Expressions.Expression<Action<AttributeJobs>>)(x => x.Run("a", null!)));
        AssertSameDefinition(options, "attr_b", (System.Linq.Expressions.Expression<Action<AttributeJobs>>)(x => x.Run("b", null!)));
        AssertSameDefinition(options, "attr_static", (System.Linq.Expressions.Expression<Func<Task>>)(() => AttributeJobs.CleanupAsync(CancellationToken.None)));

        static void AssertSameDefinition(XactJobsOptions<TestDbContext> options, string id, System.Linq.Expressions.LambdaExpression expected)
        {
            var scanned = XactJobSerializer.FromExpressionPeriodic(options.PeriodicJobs[id].JobExpression, id, CronExpressions.Daily, null);
            var written = XactJobSerializer.FromExpressionPeriodic(expected, id, CronExpressions.Daily, null);

            Assert.Equal((written.TypeName, written.MethodName, written.MethodArgs), (scanned.TypeName, scanned.MethodName, scanned.MethodArgs));
            Assert.True(written.IsCompatibleWith(scanned));
        }
    }

    [Fact]
    public void Scan_RefusesAQueueThatIsntConfigured()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AddXactJobs(o => o.WithPeriodicJobsFromAssemblyOf<AttributeJobs>()));

        Assert.Contains("periodic job 'attr_isolated': the queue 'attr' isn't configured", ex.Message);
    }

    [Fact]
    public void Scan_RefusesAnIdAlsoDefinedWithWithPeriodicJob_InAnyQueue()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => AddXactJobs(o => o
            .WithPeriodicJob<AttributeJobs>(x => x.Run("other", null!), "attr_a", CronExpressions.Hourly)
            .WithIsolatedQueue(AttributeJobs.IsolatedQueue, q => q.WithPeriodicJob(() => AttributeJobs.CleanupAsync(CancellationToken.None), "attr_static", CronExpressions.Hourly))
            .WithPeriodicJobsFromAssemblyOf<AttributeJobs>()));

        Assert.Contains("periodic job 'attr_a': the id is also defined with WithPeriodicJob", ex.Message);
        Assert.Contains("periodic job 'attr_static': the id is also defined with WithPeriodicJob", ex.Message);
    }

    [Fact]
    public void Scan_OfTheSameAssemblyTwice_AddsItsJobsOnce()
    {
        var options = AddXactJobs(o => o
            .WithIsolatedQueue(AttributeJobs.IsolatedQueue)
            .WithPeriodicJobsFromAssemblyOf<AttributeJobs>()
            .WithPeriodicJobsFromAssemblyOf<AttributeJobs>());

        Assert.Equal(3, options.PeriodicJobs.Count);
    }

    [Theory]
    [InlineData(typeof(NotPublic), "the method isn't public")]
    [InlineData(typeof(GenericMethod), "the method is generic")]
    [InlineData(typeof(GenericType<>), "its type is generic")]
    [InlineData(typeof(AbstractType), "an instance method of an abstract type")]
    [InlineData(typeof(NoPublicConstructor), "its type has no public constructor")]
    [InlineData(typeof(ReturnsInt), "it returns Int32: only void or a Task")]
    [InlineData(typeof(AsyncVoid), "it's async void")]
    [InlineData(typeof(IntParameter), "the parameter count is a Int32")]
    [InlineData(typeof(TwoStrings), "more than one string parameter")]
    [InlineData(typeof(ConfigWithoutString), "it has a config, but no string parameter for it")]
    [InlineData(typeof(InvalidCron), "invalid cron expression 'every day'")]
    [InlineData(typeof(EmptyId), "the id is empty")]
    [InlineData(typeof(DuplicateId), "the id is also on")]
    public void Find_RefusesWhatCantBeAPeriodicJob(Type type, string error)
    {
        var errors = new List<string>();

        XactPeriodicJobScanner.Find([type], errors);

        Assert.Contains(errors, e => e.Contains(error));
    }

    [Fact]
    public void Find_AcceptsAStringWithoutConfig_WhichGetsNull()
    {
        var errors = new List<string>();

        var definition = Assert.Single(XactPeriodicJobScanner.Find([typeof(StringWithoutConfig)], errors));

        Assert.Empty(errors);

        var stored = XactJobSerializer.FromExpressionPeriodic(definition.JobExpression, definition.Id, definition.CronExpression, null);

        Assert.Equal("[null]", stored.MethodArgs);
    }

    /// <summary>
    /// The jobs are ensured when the host starts, and each run gets its own config.
    /// </summary>
    [Fact]
    public async Task Host_RunsEachJobWithItsConfig()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString));

        builder.Services.AddXactJobs<TestDbContext>(options => options
            .WithWorkerCount(1)
            .WithPollingInterval(1)
            .WithIsolatedQueue(AttributeJobs.IsolatedQueue)
            .WithPeriodicJobsFromAssemblyOf<AttributeJobs>());

        builder.Services.AddScoped<AttributeJobs>();

        using var host = builder.Build();

        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
        }

        await host.StartAsync();

        try
        {
            var deadline = DateTime.UtcNow + WaitTimeout;

            while (!(AttributeJobs.Runs.Contains(("attr_a", "a")) && AttributeJobs.Runs.Contains(("attr_b", "b"))))
            {
                Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the runs of attr_a and attr_b");

                await Task.Delay(50);
            }

            Assert.DoesNotContain(AttributeJobs.Runs, r => r.PeriodicJobId == "attr_a" && r.Config != "a");
            Assert.DoesNotContain(AttributeJobs.Runs, r => r.PeriodicJobId == "attr_b" && r.Config != "b");

            using var scope = host.Services.CreateScope();

            var periodicJobs = await scope.ServiceProvider.GetRequiredService<TestDbContext>().PeriodicJobs.AsNoTracking().ToListAsync();

            Assert.Equal(AttributeJobs.IsolatedQueue, Assert.Single(periodicJobs, p => p.Id == "attr_isolated").Queue);
            Assert.Equal(QueueNames.Default, Assert.Single(periodicJobs, p => p.Id == "attr_static").Queue);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private static XactJobsOptions<TestDbContext> AddXactJobs(Action<XactJobsOptionsBuilder<TestDbContext>> configure)
    {
        var services = new ServiceCollection();

        services.AddXactJobs(configure);

        return (XactJobsOptions<TestDbContext>)services.Single(s => s.ServiceType == typeof(XactJobsOptions<TestDbContext>)).ImplementationInstance!;
    }

    // what can't be a periodic job: private nested types, so that no assembly scan finds them

    private sealed class NotPublic
    {
        [XactPeriodicJob("not_public", CronExpressions.Daily)]
        internal void Run() { }
    }

    private sealed class GenericMethod
    {
        [XactPeriodicJob("generic_method", CronExpressions.Daily)]
        public void Run<T>() { }
    }

    private sealed class GenericType<T>
    {
        [XactPeriodicJob("generic_type", CronExpressions.Daily)]
        public void Run() { }
    }

    private abstract class AbstractType
    {
        [XactPeriodicJob("abstract_type", CronExpressions.Daily)]
        public void Run() { }
    }

    private sealed class NoPublicConstructor
    {
        private NoPublicConstructor() { }

        [XactPeriodicJob("no_public_constructor", CronExpressions.Daily)]
        public void Run() { }
    }

    private sealed class ReturnsInt
    {
        [XactPeriodicJob("returns_int", CronExpressions.Daily)]
        public int Run() => 0;
    }

    private sealed class AsyncVoid
    {
#pragma warning disable VSTHRD100 // the point of the test
        [XactPeriodicJob("async_void", CronExpressions.Daily)]
        public async void Run() => await Task.Yield();
#pragma warning restore VSTHRD100
    }

    private sealed class IntParameter
    {
        [XactPeriodicJob("int_parameter", CronExpressions.Daily)]
        public void Run(int count) { }
    }

    private sealed class TwoStrings
    {
        [XactPeriodicJob("two_strings", CronExpressions.Daily, Config = "c")]
        public void Run(string a, string b) { }
    }

    private sealed class ConfigWithoutString
    {
        [XactPeriodicJob("config_without_string", CronExpressions.Daily, Config = "c")]
        public void Run(CancellationToken cancellationToken) { }
    }

    private sealed class InvalidCron
    {
        [XactPeriodicJob("invalid_cron", "every day")]
        public void Run() { }
    }

    private sealed class EmptyId
    {
        [XactPeriodicJob("", CronExpressions.Daily)]
        public void Run() { }
    }

    private sealed class DuplicateId
    {
        [XactPeriodicJob("duplicate", CronExpressions.Daily)]
        public void Run() { }

        [XactPeriodicJob("duplicate", CronExpressions.Hourly)]
        public void RunToo() { }
    }

    private sealed class StringWithoutConfig
    {
        [XactPeriodicJob("string_without_config", CronExpressions.Daily)]
        public void Run(string? config) { }
    }
}
