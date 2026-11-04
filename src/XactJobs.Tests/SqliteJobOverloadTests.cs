using System.Collections.Concurrent;
using System.Text.Json;
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
    public async Task JobWithoutASignature_RunsTheOnlyMethodWithItsNameAndParameterCount()
    {
        // jobs stored before 0.1.7 only have the method's name
        var id = await AddRawJobAsync(nameof(OverloadedJob.NotOverloaded), "x");

        var history = await RunAndGetHistoryAsync(id);

        Assert.Equal(XactJobStatus.Completed, history.Single().Status);
        Assert.Equal(["not overloaded x"], Recorder.Calls);
    }

    [Fact]
    public async Task JobWithAnOutdatedSignature_RunsTheOnlyMethodWithItsNameAndParameterCount()
    {
        // e.g. the parameter's type was renamed after the job was enqueued
        var id = await AddRawJobAsync("NotOverloaded(Old.Namespace.Message)", "y");

        var history = await RunAndGetHistoryAsync(id);

        Assert.Equal(XactJobStatus.Completed, history.Single().Status);
        Assert.Equal(["not overloaded y"], Recorder.Calls);
    }

    [Fact]
    public async Task JobWithoutASignature_OnAnOverloadedMethod_Fails()
    {
        var id = await AddRawJobAsync(nameof(OverloadedJob.Record), "z");

        var history = await RunAndGetHistoryAsync(id);

        Assert.Equal(XactJobStatus.Failed, history.Single().Status);
        Assert.Contains("several public methods named 'Record' with 1 parameter(s)", history.Single().ErrorMessage);
        Assert.Empty(Recorder.Calls);
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

    private Task<long> AddRawJobAsync(string methodName, string arg)
    {
        return WithDbAsync(async db =>
        {
            var typeName = db.JobEnqueue<OverloadedJob>(x => x.NotOverloaded("template")).TypeName;

            db.ChangeTracker.Clear();

            var job = new XactJob(0, DateTime.UtcNow, typeName, methodName, JsonSerializer.Serialize(new object[] { arg }), QueueNames.Default);

            db.Jobs.Add(job);

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

        public void NotOverloaded(string value) => recorder.Add($"not overloaded {value}");
    }

    public sealed class HidingJob(CallRecorder recorder) : OverloadedJob(recorder)
    {
        public new void Record(string value) => Recorder.Add($"hiding {value}");
    }
}
