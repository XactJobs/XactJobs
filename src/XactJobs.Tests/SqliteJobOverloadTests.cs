using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using XactJobs.Internal;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace XactJobs.Tests;

public class SqliteJobOverloadTests : IAsyncLifetime
{
    private const string Daily = "0 0 0 * * *";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    // this connection keeps the memory DB alive during the test
    private readonly SqliteConnection _memoryConnection;
    private readonly string _connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

    private IHost? _host;

    public SqliteJobOverloadTests()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
    }

    public async Task InitializeAsync()
    {
        await _memoryConnection.OpenAsync();

        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString));

        builder.Services.AddXactJobs<TestDbContext>(options => options.WithWorkerCount(1).WithPollingInterval(1));

        builder.Services.AddSingleton<CallRecorder>();
        builder.Services.AddScoped<OverloadedJob>();
        builder.Services.AddScoped<HidingJob>();
        builder.Services.AddScoped<OptionalJob>();

        _host = builder.Build();

        await WithDbAsync(db => db.Database.EnsureCreatedAsync());
    }

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        await _memoryConnection.DisposeAsync();
    }

    private CallRecorder Recorder => _host!.Services.GetRequiredService<CallRecorder>();

    [Fact]
    public async Task Enqueue_StoresTheMethodsSignature()
    {
        var jobs = await WithDbAsync(async db =>
        {
            var list = new[]
            {
                db.JobEnqueue<OverloadedJob>(x => x.Record("a")),
                db.JobEnqueue<OverloadedJob>(x => x.Record(1)),
                db.JobEnqueue<OverloadedJob>(x => x.Record((int?)1)),
                db.JobEnqueue<OverloadedJob>(x => x.Record(new List<int> { 1 })),
                db.JobEnqueue<OverloadedJob>(x => x.Record(new[] { 1 })),
                db.JobEnqueue<OverloadedJob>(x => x.RecordAsync("a", CancellationToken.None)),
            };

            await db.SaveChangesAsync();

            return list;
        });

        Assert.Equal(
        [
            "Record(System.String)",
            "Record(System.Int32)",
            "Record(System.Nullable`1[System.Int32])",
            "Record(System.Collections.Generic.List`1[System.Int32])",
            "Record(System.Int32[])",
            "RecordAsync(System.String, System.Threading.CancellationToken)",
        ], jobs.Select(j => j.MethodName));
    }

    [Fact]
    public async Task Overloads_WithTheSameParameterCount_RunTheEnqueuedOverload()
    {
        var ids = await WithDbAsync(async db =>
        {
            var list = new[]
            {
                db.JobEnqueue<OverloadedJob>(x => x.Record("a")),
                db.JobEnqueue<OverloadedJob>(x => x.Record(1)),
                db.JobEnqueue<OverloadedJob>(x => x.Record((int?)2)),
                db.JobEnqueue<OverloadedJob>(x => x.Record(new List<int> { 3, 4 })),
                db.JobEnqueue<OverloadedJob>(x => x.Record(new[] { 5 })),
                db.JobEnqueue<OverloadedJob>(x => x.RecordAsync("b", CancellationToken.None)),
                db.JobEnqueue<OverloadedJob>(x => x.RecordAsync(6, CancellationToken.None)),
            };

            await db.SaveChangesAsync();

            return list.Select(j => j.Id).ToArray();
        });

        var history = await RunAndGetHistoryAsync(ids);

        Assert.All(history, h => Assert.Equal(XactJobStatus.Completed, h.Status));

        // the worker runs them in any order
        Assert.Equal(new[]
        {
            "string a",
            "int 1",
            "int? 2",
            "list 3,4",
            "array 5",
            "async string b",
            "async int 6",
        }.Order(), Recorder.Calls.Order());
    }

    [Fact]
    public async Task OverloadedPeriodicJob_RunsItsOverload()
    {
        var jobId = await WithDbAsync(async db =>
        {
            db.JobEnsurePeriodic<OverloadedJob>(x => x.Record(7), "overloaded", Daily);

            await db.SaveChangesAsync();

            // make it due: the scheduled time isn't part of the key on Sqlite
            await db.Jobs.ExecuteUpdateAsync(s => s.SetProperty(j => j.ScheduledAt, DateTime.UtcNow.AddMinutes(-1)));

            return (await db.Jobs.AsNoTracking().SingleAsync(j => j.PeriodicJobId == "overloaded")).Id;
        });

        var history = await RunAndGetHistoryAsync(jobId);

        Assert.Equal(XactJobStatus.Completed, history.Single().Status);
        Assert.Equal(["int 7"], Recorder.Calls);
    }

    [Fact]
    public async Task MethodHiddenWithNew_RunsTheHidingMethod()
    {
        var id = await EnqueueAsync(db => db.JobEnqueue<HidingJob>(x => x.Record("c")));

        var history = await RunAndGetHistoryAsync(id);

        Assert.Equal(XactJobStatus.Completed, history.Single().Status);
        Assert.Equal(["hiding c"], Recorder.Calls);
    }

    [Fact]
    public async Task ConsoleWriteLine_CanBeAJob()
    {
        var id = await EnqueueAsync(db => db.JobEnqueue(() => Console.WriteLine("Hello from a job")));

        var history = await RunAndGetHistoryAsync(id);

        Assert.Equal(XactJobStatus.Completed, history.Single().Status);
    }

    [Fact]
    public async Task JobStoredBeforeOptionalParametersWereAdded_RunsTheMethod_WithTheirDefaults()
    {
        // as enqueued when the method was Send(string): its stored signature and arguments
        var ids = new[]
        {
            await AddStoredJobAsync<OptionalJob>("Send(System.String)", "[\"a\"]"),
            await AddStoredJobAsync<OptionalJob>("Note(System.String)", "[\"b\"]"),
        };

        var history = await RunAndGetHistoryAsync(ids);

        Assert.All(history, h => Assert.Equal(XactJobStatus.Completed, h.Status));
        Assert.Equal(new[] { "send a True Friday 1.5 0001-01-01 5 token context", "note b null 0" }.Order(), Recorder.Calls.Order());
    }

    [Fact]
    public async Task JobWithTheStoredSignature_RunsItsMethod_NotOneWithDefaults()
    {
        var ids = new[]
        {
            await AddStoredJobAsync<OptionalJob>("Exact(System.String)", "[\"b\"]"),
            await EnqueueAsync(db => db.JobEnqueue<OptionalJob>(x => x.Exact("c", true))),
        };

        var history = await RunAndGetHistoryAsync(ids);

        Assert.All(history, h => Assert.Equal(XactJobStatus.Completed, h.Status));
        Assert.Equal(new[] { "exact b", "exact c True" }.Order(), Recorder.Calls.Order());
    }

    [Theory]
    [InlineData("Ambiguous(System.String)", "is ambiguous", "Ambiguous(System.String, System.Boolean), Ambiguous(System.String, System.Int32)")]
    [InlineData("Required(System.String)", "wasn't found", "no public method with this signature")]
    [InlineData("Send(System.Int32)", "wasn't found", "no public method with this signature")]
    public void StoredSignature_WithoutOneMethodThatAddsOnlyOptionalParameters_IsntFound(string methodName, string error, string detail)
    {
        var ex = Assert.Throws<XactJobNotFoundException>(() => StoredJob<OptionalJob>(methodName, "[]").ToMethodInfo());

        Assert.Contains(error, ex.Message);
        Assert.Contains(detail, ex.Message);
    }

    [Fact]
    public void MethodWithDefaults_AndItsOverride_AreOneCandidate_TheOverride()
    {
        var (_, method) = StoredJob<DerivedOptionalJob>("Virtual(System.String)", "[]").ToMethodInfo();

        Assert.Equal(typeof(DerivedOptionalJob), method.DeclaringType);
    }

    private static XactJob StoredJob<T>(string methodName, string methodArgs)
    {
        var type = typeof(T);

        return new XactJob(0, DateTime.UtcNow.AddMinutes(-1), $"{type.FullName}, {type.Assembly.GetName().Name}", methodName, methodArgs, QueueNames.Default);
    }

    private Task<long> AddStoredJobAsync<T>(string methodName, string methodArgs)
    {
        return EnqueueAsync(db => db.Jobs.Add(StoredJob<T>(methodName, methodArgs)).Entity);
    }

    private Task<long> EnqueueAsync(Func<TestDbContext, XactJob> enqueue)
    {
        return WithDbAsync(async db =>
        {
            var job = enqueue(db);

            await db.SaveChangesAsync();

            return job.Id;
        });
    }

    private async Task<XactJobHistory[]> RunAndGetHistoryAsync(params long[] jobIds)
    {
        await _host!.StartAsync();

        var deadline = DateTime.UtcNow + WaitTimeout;

        while (await WithDbAsync(db => db.JobHistory.Select(h => h.Id).Distinct().CountAsync(id => jobIds.Contains(id))) < jobIds.Length)
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the history rows");

            await Task.Delay(50);
        }

        return await WithDbAsync(db => db.JobHistory.AsNoTracking().Where(h => jobIds.Contains(h.Id)).ToArrayAsync());
    }

    private async Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        using var scope = _host!.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }

    public sealed class CallRecorder
    {
        private readonly ConcurrentQueue<string> _calls = new();

        public IReadOnlyCollection<string> Calls => _calls.ToArray();

        public void Add(string call) => _calls.Enqueue(call);
    }

    public class OverloadedJob(CallRecorder recorder)
    {
        protected CallRecorder Recorder => recorder;

        public void Record(string value) => recorder.Add($"string {value}");

        public void Record(int value) => recorder.Add($"int {value}");

        public void Record(int? value) => recorder.Add($"int? {value}");

        public void Record(List<int> values) => recorder.Add($"list {string.Join(",", values)}");

        public void Record(int[] values) => recorder.Add($"array {string.Join(",", values)}");

        public async Task RecordAsync(string value, CancellationToken cancellationToken)
        {
            await Task.Yield();

            recorder.Add($"async string {value}");
        }

        public async Task RecordAsync(int value, CancellationToken cancellationToken)
        {
            await Task.Yield();

            recorder.Add($"async int {value}");
        }
    }

    public class OptionalJob(CallRecorder recorder)
    {
        public void Send(string to, bool urgent = true, DayOfWeek day = DayOfWeek.Friday, decimal amount = 1.5m, DateTime at = default,
                         int? count = 5, CancellationToken cancellationToken = default, XactJobContext? context = null)
        {
            recorder.Add(string.Join(" ", "send", to, urgent, day, amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                                     at.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), count,
                                     cancellationToken.CanBeCanceled ? "token" : "no token", context != null ? "context" : "no context"));
        }

        // C# allows [Optional] parameters only after the required ones, before any with a default value
        public void Note(string value, [Optional] string? note, [Optional] int count) => recorder.Add($"note {value} {note ?? "null"} {count}");

        public void Exact(string value) => recorder.Add($"exact {value}");

        public void Exact(string value, bool flag = false) => recorder.Add($"exact {value} {flag}");

        public void Ambiguous(string value, bool flag = false) { }

        public void Ambiguous(string value, int count = 0) { }

        public void Required(string value, int count) { }

        public virtual void Virtual(string value, bool flag = false) { }
    }

    public sealed class DerivedOptionalJob(CallRecorder recorder) : OptionalJob(recorder)
    {
        public override void Virtual(string value, bool flag = false) { }
    }

    public sealed class HidingJob(CallRecorder recorder) : OverloadedJob(recorder)
    {
        public new void Record(string value) => Recorder.Add($"hiding {value}");
    }
}
