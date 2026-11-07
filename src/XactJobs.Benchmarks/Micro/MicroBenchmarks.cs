using System.Linq.Expressions;
using BenchmarkDotNet.Attributes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using XactJobs.CronUtil;
using XactJobs.Internal;

namespace XactJobs.Benchmarks.Micro;

/// <summary>
/// What <c>JobEnqueue</c> does before EF adds the row: the expression tree (built by the compiler at the call site, as in an app),
/// evaluating the arguments, and serializing them.
/// </summary>
[MemoryDiagnoser]
public class EnqueueBenchmarks
{
    private string _to = "alice@acme.com";
    private int _orderId = 42;
    private Order _order = new(42, "ACME", 129.90m, ["apples", "pears", "plums"]);

    [Benchmark(Baseline = true)]
    public XactJob StaticNoArguments() => Serialize(() => BenchmarkJobs.Noop());

    [Benchmark]
    public XactJob StaticWithArguments() => Serialize(() => BenchmarkJobs.Send(_to, _orderId));

    [Benchmark]
    public XactJob StaticWithAnObject() => Serialize(() => BenchmarkJobs.Process(_order));

    [Benchmark]
    public XactJob InstanceWithContextAndToken() => Serialize<BenchmarkJobs>(x => x.RunAsync(_orderId, null!, CancellationToken.None));

    private static XactJob Serialize(Expression<Action> job) => XactJobSerializer.FromExpression(job, null, null);

    private static XactJob Serialize<T>(Expression<Func<T, Task>> job) => XactJobSerializer.FromExpression(job, null, null);
}

/// <summary>
/// What the worker does to run a job, without the database: reading the arguments, finding the compiled delegate, and calling it.
/// The scope is created once: creating a scope and the DbContext for each job is in the drain benchmark.
/// </summary>
[MemoryDiagnoser]
public class RunBenchmarks
{
    private ServiceProvider _services = null!;
    private IServiceScope _scope = null!;

    private XactJob _noArguments = null!;
    private XactJob _withArguments = null!;
    private XactJob _withAnObject = null!;
    private XactJob _instance = null!;

    private readonly XactJobContext _context = new(DateTime.UtcNow);

    [GlobalSetup]
    public async Task SetupAsync()
    {
        _services = new ServiceCollection().AddSingleton<JobDependency>().BuildServiceProvider();
        _scope = _services.CreateScope();

        var to = "alice@acme.com";
        var orderId = 42;
        var order = new Order(42, "ACME", 129.90m, ["apples", "pears", "plums"]);

        _noArguments = XactJobSerializer.FromExpression((Expression<Action>)(() => BenchmarkJobs.Noop()), null, null);
        _withArguments = XactJobSerializer.FromExpression((Expression<Action>)(() => BenchmarkJobs.Send(to, orderId)), null, null);
        _withAnObject = XactJobSerializer.FromExpression((Expression<Action>)(() => BenchmarkJobs.Process(order)), null, null);
        _instance = XactJobSerializer.FromExpression((Expression<Func<BenchmarkJobs, Task>>)(x => x.RunAsync(orderId, null!, CancellationToken.None)), null, null);

        // compiles the delegates, which the worker does once per job method
        foreach (var job in new[] { _noArguments, _withArguments, _withAnObject, _instance })
        {
            await XactJobCompiler.CompileAndRunJobAsync(_scope, job, _context, CancellationToken.None);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _services.Dispose();
    }

    [Benchmark(Baseline = true)]
    public Task StaticNoArguments() => XactJobCompiler.CompileAndRunJobAsync(_scope, _noArguments, _context, CancellationToken.None);

    [Benchmark]
    public Task StaticWithArguments() => XactJobCompiler.CompileAndRunJobAsync(_scope, _withArguments, _context, CancellationToken.None);

    [Benchmark]
    public Task StaticWithAnObject() => XactJobCompiler.CompileAndRunJobAsync(_scope, _withAnObject, _context, CancellationToken.None);

    [Benchmark]
    public Task InstanceWithContextAndToken() => XactJobCompiler.CompileAndRunJobAsync(_scope, _instance, _context, CancellationToken.None);
}

/// <summary>
/// The next run of a periodic job, as <c>ScheduleNextRun</c> computes it after each run: parsing the expression, and the next slot.
/// </summary>
[MemoryDiagnoser]
public class CronBenchmarks
{
    private readonly DateTime _now = new(2026, 11, 7, 13, 37, 21, DateTimeKind.Utc);

    [Benchmark(Baseline = true)]
    public DateTime EveryFiveMinutes() => new CronSequenceGenerator("0 */5 * * * *", TimeZoneInfo.Utc).NextUtc(_now);

    [Benchmark]
    public DateTime Daily() => new CronSequenceGenerator(Cron.Daily(), TimeZoneInfo.Utc).NextUtc(_now);

    [Benchmark]
    public DateTime WeekdaysAtNine() => new CronSequenceGenerator("0 0 9 * * MON-FRI", TimeZoneInfo.Utc).NextUtc(_now);
}

/// <summary>
/// The check of the job's saves outside a transaction (<see cref="XactJobSaveCheck"/>), which the worker subscribes for each job and unsubscribes after it.
/// </summary>
[MemoryDiagnoser]
public class SaveCheckBenchmarks
{
    private readonly XactJobSaveChecker _warn = new(XactJobSaveCheck.Warn);
    private readonly XactJobSaveChecker _ignore = new(XactJobSaveCheck.Ignore);

    private readonly XactJob _job = XactJobSerializer.FromExpression((Expression<Action>)(() => BenchmarkJobs.Noop()), null, null);

    private BenchDbContext _db = null!;

    [GlobalSetup]
    public void Setup()
    {
        // the connection is never opened: subscribing doesn't need it
        _db = new BenchDbContext(new DbContextOptionsBuilder<BenchDbContext>().UseSqlite("Data Source=:memory:").Options);
    }

    [GlobalCleanup]
    public void Cleanup() => _db.Dispose();

    [Benchmark(Baseline = true)]
    public void Warn() => XactJobSaveChecker.Unsubscribe(_db, _warn.Subscribe(_db, _job, NullLogger.Instance, QueueNames.Default));

    [Benchmark]
    public void Ignore() => XactJobSaveChecker.Unsubscribe(_db, _ignore.Subscribe(_db, _job, NullLogger.Instance, QueueNames.Default));
}
