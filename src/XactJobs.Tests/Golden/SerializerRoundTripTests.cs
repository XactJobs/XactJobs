using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace XactJobs.Tests.Golden;

/// <summary>
/// Jobs run by a worker get their arguments back as they were enqueued, and the method that runs is the one that was enqueued:
/// on nested and generic types, and inherited and overridden methods. (A job on an interface is stored, see SerializerFormatTests,
/// but can't run: the worker creates the job's class with its public constructor, not from DI.)
/// </summary>
public class SerializerRoundTripTests : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private readonly string _connectionString = $"Data Source=roundtrip-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;";
    private SqliteConnection? _memoryConnection;
    private IHost? _host;

    public async Task InitializeAsync()
    {
        _memoryConnection = new SqliteConnection(_connectionString);
        await _memoryConnection.OpenAsync();

        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddDbContext<TestDbContext>(options => options.UseSqlite(_connectionString));
        builder.Services.AddXactJobs<TestDbContext>(options => options.WithPollingInterval(1).WithWorkerCount(1));

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

        if (_memoryConnection != null) await _memoryConnection.DisposeAsync();
    }

    [Fact]
    public async Task Arguments_ComeBackAsTheyWereEnqueued()
    {
        var key = Guid.NewGuid().ToString("N");
        var utc = new DateTime(2026, 3, 14, 15, 9, 26, 535, DateTimeKind.Utc);
        var unspecified = new DateTime(2026, 3, 14, 15, 9, 26, DateTimeKind.Unspecified);
        var offset = new DateTimeOffset(2026, 3, 14, 15, 9, 26, TimeSpan.FromHours(2));
        var id = Guid.NewGuid();
        var text = "<b>\"quote\" & 'apostrophe'</b>\n\ttab \\ é ü ß 日本 😀";

        await WithDbAsync(async db =>
        {
            db.JobEnqueue(() => RoundTripJobs.Primitives(key, "text", -42, 9_007_199_254_740_993L, true, 1.5, 12.345m, 'x', 255, -7, 0.25f, 4_000_000_000u, 18_000_000_000_000_000_000ul));
            db.JobEnqueue(() => RoundTripJobs.Times(key, utc, unspecified, offset, TimeSpan.FromMinutes(90.5), new DateOnly(2026, 3, 14), new TimeOnly(15, 9, 26)));
            db.JobEnqueue(() => RoundTripJobs.Values(key, id, Priority.High, null, 3, null, text));
            db.JobEnqueue(() => RoundTripJobs.Collections(key, new[] { 1, 2, 3 }, new List<string> { "x", "y" }, new Dictionary<string, int> { { "one", 1 } }));
            db.JobEnqueue(() => RoundTripJobs.Objects(key, new Address { Street = "Main", Number = 5 }, new Money(9.99m, "EUR"), null));
            db.JobEnqueue(() => RoundTripJobs.Params(key, "a", "b"));
            return await db.SaveChangesAsync();
        });

        await _host!.StartAsync();

        var calls = await WaitForCallsAsync(key, 6);

        Assert.Equal(new object?[] { "text", -42, 9_007_199_254_740_993L, true, 1.5, 12.345m, 'x', (byte)255, (short)-7, 0.25f, 4_000_000_000u, 18_000_000_000_000_000_000ul }, calls["Primitives"]);

        var times = calls["Times"];
        Assert.Equal(utc, times[0]);
        Assert.Equal(DateTimeKind.Utc, ((DateTime)times[0]!).Kind);
        Assert.Equal(unspecified, times[1]);
        Assert.Equal(offset, times[2]);
        Assert.Equal(TimeSpan.FromMinutes(90.5), times[3]);
        Assert.Equal(new DateOnly(2026, 3, 14), times[4]);
        Assert.Equal(new TimeOnly(15, 9, 26), times[5]);

        Assert.Equal(new object?[] { id, Priority.High, null, 3, null, text }, calls["Values"]);

        var collections = calls["Collections"];
        Assert.Equal(new[] { 1, 2, 3 }, collections[0]);
        Assert.Equal(new List<string> { "x", "y" }, collections[1]);
        Assert.Equal(new Dictionary<string, int> { { "one", 1 } }, collections[2]);

        var objects = calls["Objects"];
        var address = Assert.IsType<Address>(objects[0]);
        Assert.Equal(("Main", 5, (string?)null), (address.Street, address.Number, address.Note));
        Assert.Equal(new Money(9.99m, "EUR"), objects[1]);
        Assert.Null(objects[2]);

        Assert.Equal(new[] { "a", "b" }, calls["Params"][0]);
    }

    [Fact]
    public async Task TheEnqueuedMethodRuns_OnEveryKindOfType()
    {
        var key = Guid.NewGuid().ToString("N");

        await WithDbAsync(async db =>
        {
            db.JobEnqueue(() => RoundTripJobs.Outer.Nested(key));
            db.JobEnqueue(() => RoundTripGeneric<string>.Static(key));
            db.JobEnqueue<RoundTripGeneric<int>>(x => x.Instance(key, 4));
            db.JobEnqueue<RoundTripDerived>(x => x.Inherited(key));
            db.JobEnqueue<RoundTripDerived>(x => x.Virtual(key));
            db.JobEnqueue<RoundTripBase>(x => x.Virtual(key));
            return await db.SaveChangesAsync();
        });

        await _host!.StartAsync();

        var calls = await WaitForCallsAsync(key, 6);

        Assert.Equal(
            ["Base.Inherited on RoundTripDerived", "Base.Virtual", "Derived.Virtual", "Generic<Int32>.Instance", "Generic<String>.Static", "Nested"],
            calls.Keys.Order());
        Assert.Equal(4, calls["Generic<Int32>.Instance"][0]);
    }

    /// <summary>Waits for the jobs' calls; if they don't all come, fails with the errors of the jobs that failed.</summary>
    private async Task<IReadOnlyDictionary<string, object?[]>> WaitForCallsAsync(string key, int count)
    {
        try
        {
            return await RoundTripJobs.WaitForAsync(key, count).WaitAsync(WaitTimeout);
        }
        catch (TimeoutException)
        {
            var failures = await WithDbAsync(db => db.JobHistory.AsNoTracking()
                .Where(h => h.Status != XactJobStatus.Completed)
                .Select(h => h.TypeName + " " + h.MethodName + ": " + h.ErrorMessage)
                .ToListAsync());

            throw new Xunit.Sdk.XunitException($"Not all the jobs ran. Failed: {string.Join(" | ", failures)}");
        }
    }

    private async Task<T> WithDbAsync<T>(Func<TestDbContext, Task<T>> action)
    {
        using var scope = _host!.Services.CreateScope();

        return await action(scope.ServiceProvider.GetRequiredService<TestDbContext>());
    }
}

/// <summary>Jobs that record the arguments they get, by a key per test.</summary>
public static class RoundTripJobs
{
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, object?[]>> Calls = new();

    public static void Record(string key, string call, params object?[] args) => Calls.GetOrAdd(key, _ => new())[call] = args;

    public static async Task<IReadOnlyDictionary<string, object?[]>> WaitForAsync(string key, int count)
    {
        while (!Calls.TryGetValue(key, out var calls) || calls.Count < count)
        {
            await Task.Delay(50);
        }

        return Calls[key];
    }

    public static void Primitives(string key, string s, int i, long l, bool b, double d, decimal m, char c, byte by, short sh, float f, uint ui, ulong ul) =>
        Record(key, nameof(Primitives), s, i, l, b, d, m, c, by, sh, f, ui, ul);

    public static void Times(string key, DateTime utc, DateTime unspecified, DateTimeOffset offset, TimeSpan span, DateOnly date, TimeOnly time) =>
        Record(key, nameof(Times), utc, unspecified, offset, span, date, time);

    public static void Values(string key, Guid id, Priority priority, int? none, int? some, string? nothing, string text) =>
        Record(key, nameof(Values), id, priority, none, some, nothing, text);

    public static void Collections(string key, int[] numbers, List<string> list, Dictionary<string, int> map) =>
        Record(key, nameof(Collections), numbers, list, map);

    public static void Objects(string key, Address address, Money money, Address? missing) =>
        Record(key, nameof(Objects), address, money, missing);

    public static void Params(string key, params string[] items) => Record(key, nameof(Params), [items]);

    public static class Outer
    {
        public static void Nested(string key) => Record(key, "Nested");
    }
}

public class RoundTripGeneric<T>
{
    public static void Static(string key) => RoundTripJobs.Record(key, $"Generic<{typeof(T).Name}>.Static");

    public void Instance(string key, T value) => RoundTripJobs.Record(key, $"Generic<{typeof(T).Name}>.Instance", value);
}

public class RoundTripBase
{
    public void Inherited(string key) => RoundTripJobs.Record(key, $"Base.Inherited on {GetType().Name}");

    public virtual void Virtual(string key) => RoundTripJobs.Record(key, "Base.Virtual");
}

public class RoundTripDerived : RoundTripBase
{
    public override void Virtual(string key) => RoundTripJobs.Record(key, "Derived.Virtual");
}
