using Microsoft.EntityFrameworkCore;

namespace XactJobs.Tests.Golden;

/// <summary>
/// The exact strings a job is stored as: its type, its method with the parameter types, and its arguments as JSON. Databases hold
/// these strings, so they must never change: an older pending job must still run, and a periodic job is compared with its
/// definition by them (a different string for the same job would look like a changed definition, and skip its pending run).
/// </summary>
public class SerializerFormatTests : IDisposable
{
    private readonly TestDbContext _db;

    public SerializerFormatTests()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>().UseSqlite("DataSource=:memory:").Options;

        _db = new TestDbContext(options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    private static readonly DateTime Utc = new(2026, 3, 14, 15, 9, 26, 535, DateTimeKind.Utc);
    private static readonly int Field = 7;
    private static int Property => 8;

    /// <summary>The cases: each one enqueues a job, whose strings are compared with <see cref="Expected"/>.</summary>
    public static readonly (string Name, Func<TestDbContext, object> Enqueue)[] Cases =
    [
        ("static, no arguments", db => db.JobEnqueue(() => StaticJobs.NoArgs())),
        ("primitives", db => db.JobEnqueue(() => StaticJobs.Primitives("text", -42, 9_007_199_254_740_993L, true, 1.5, 12.345m, 'x', 255, -7, 0.25f, 4_000_000_000u, 18_000_000_000_000_000_000ul))),
        ("nullable values", db => db.JobEnqueue(() => StaticJobs.NullableValues(null, 3, null))),
        ("null string", db => db.JobEnqueue(() => StaticJobs.NullString(null))),
        ("text that JSON escapes", db => db.JobEnqueue(() => StaticJobs.Text("<b>\"quote\" & 'apostrophe'</b>\n\ttab \\ é ü ß 日本 😀"))),
        ("times", db => db.JobEnqueue(() => StaticJobs.Times(
            Utc,
            new DateTime(2026, 3, 14, 15, 9, 26, DateTimeKind.Unspecified),
            new DateTimeOffset(2026, 3, 14, 15, 9, 26, TimeSpan.FromHours(2)),
            TimeSpan.FromMinutes(90.5),
            new DateOnly(2026, 3, 14),
            new TimeOnly(15, 9, 26)))),
        ("ids and enums", db => db.JobEnqueue(() => StaticJobs.Ids(Guid.Parse("3f2504e0-4f89-11d3-9a0c-0305e82c3301"), Priority.High, DayOfWeek.Friday))),
        ("collections", db => db.JobEnqueue(() => StaticJobs.Collections(
            new[] { 1, 2, 3 },
            new[] { "a", "b" },
            new List<string> { "x" },
            new Dictionary<string, int> { { "one", 1 }, { "two", 2 } },
            new List<long> { 5 }))),
        ("params", db => db.JobEnqueue(() => StaticJobs.Params("a", "b", "c"))),
        ("params, none", db => db.JobEnqueue(() => StaticJobs.Params())),
        ("objects", db => db.JobEnqueue(() => StaticJobs.Objects(
            new Address { Street = "Main", Number = 5 },
            new Money(9.99m, "EUR"),
            new Address { Street = "Boxed", Number = 1 },
            null))),
        ("cancellation token", db => db.JobEnqueue(() => StaticJobs.WithTokenAsync(3, CancellationToken.None))),
        ("job context", db => db.JobEnqueue(() => StaticJobs.WithContextAsync(null!, CancellationToken.None))),
        ("overload, string", db => db.JobEnqueue(() => StaticJobs.Overload("a"))),
        ("overload, int", db => db.JobEnqueue(() => StaticJobs.Overload(1))),
        ("overload, string and int", db => db.JobEnqueue(() => StaticJobs.Overload("a", 1))),
        ("computed arguments", db => db.JobEnqueue(() => StaticJobs.Overload(StaticJobs.Compute(2)))),
        ("captured variables", db => CapturedVariables(db)),
        ("fields and properties", db => db.JobEnqueue(() => StaticJobs.Overload("p" + Field + Property, Field * Property))),
        ("conditional and interpolation", db => Conditional(db, true)),
        ("instance", db => db.JobEnqueue<InstanceJobs>(x => x.Run())),
        ("instance overload", db => db.JobEnqueue<InstanceJobs>(x => x.Run("name"))),
        ("instance async", db => db.JobEnqueue<InstanceJobs>(x => x.RunAsync(5))),
        ("instance, Task<T>", db => db.JobEnqueue<InstanceJobs>(x => x.CountAsync())),
        ("inherited method on a derived type", db => db.JobEnqueue<DerivedJobs>(x => x.Run("inherited"))),
        ("override on a derived type", db => db.JobEnqueue<DerivedJobs>(x => x.Virtual())),
        ("virtual on the base type", db => db.JobEnqueue<InstanceJobs>(x => x.Virtual())),
        ("interface", db => db.JobEnqueue<IJobService>(x => x.ProcessAsync(9, CancellationToken.None))),
        ("nested type", db => db.JobEnqueue(() => Outer.Nested.Run(1))),
        ("generic type", db => db.JobEnqueue(() => GenericHolder<string>.Run(1))),
        ("generic instance type", db => db.JobEnqueue<GenericInstance<int>>(x => x.Run(4))),
        ("framework method", db => db.JobEnqueue(() => Console.WriteLine("hello"))),
        ("internal type", db => db.JobEnqueue(() => InternalJobs.Run())),
        ("queue", db => db.JobEnqueue(() => StaticJobs.NoArgs(), QueueNames.Priority)),
        ("scheduled", db => db.JobScheduleAt(() => StaticJobs.Overload(2), Utc)),
        ("batch job", db => db.JobBatch().Enqueue(() => StaticJobs.Overload(3)).Jobs[0]),
        ("continuation", db => db.JobEnqueue(() => StaticJobs.NoArgs()).ContinueWith<InstanceJobs>(x => x.RunAsync(6))),
        ("periodic", db => db.JobEnsurePeriodic<InstanceJobs>(x => x.RunAsync(7), "golden_periodic", Cron.Daily())),
    ];

    private static XactJob CapturedVariables(TestDbContext db)
    {
        var name = "captured";
        var count = 3;
        var address = new Address { Street = "Side", Number = count };

        return db.JobEnqueue(() => StaticJobs.Objects(address, new Money(count, name), name, null));
    }

    private static XactJob Conditional(TestDbContext db, bool flag)
    {
        var id = 12;

        return db.JobEnqueue(() => StaticJobs.Overload(flag ? $"id-{id}" : "none", flag ? id : 0));
    }

    /// <summary>The strings the cases are stored as: (TypeName, MethodName, MethodArgs).</summary>
    public static readonly Dictionary<string, (string TypeName, string MethodName, string MethodArgs)> Expected = new()
    {
        ["static, no arguments"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "NoArgs()",
            "[]"),
        ["primitives"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Primitives(System.String, System.Int32, System.Int64, System.Boolean, System.Double, System.Decimal, System.Char, System.Byte, System.Int16, System.Single, System.UInt32, System.UInt64)",
            "[\"text\",-42,9007199254740993,true,1.5,12.345,\"x\",255,-7,0.25,4000000000,18000000000000000000]"),
        ["nullable values"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "NullableValues(System.Nullable`1[System.Int32], System.Nullable`1[System.Int32], System.Nullable`1[System.DateTime])",
            "[null,3,null]"),
        ["null string"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "NullString(System.String)",
            "[null]"),
        ["text that JSON escapes"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Text(System.String)",
            "[\"\\u003Cb\\u003E\\u0022quote\\u0022 \\u0026 \\u0027apostrophe\\u0027\\u003C/b\\u003E\\n\\ttab \\\\ \\u00E9 \\u00FC \\u00DF \\u65E5\\u672C \\uD83D\\uDE00\"]"),
        ["times"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Times(System.DateTime, System.DateTime, System.DateTimeOffset, System.TimeSpan, System.DateOnly, System.TimeOnly)",
            "[\"2026-03-14T15:09:26.535Z\",\"2026-03-14T15:09:26\",\"2026-03-14T15:09:26+02:00\",\"01:30:30\",\"2026-03-14\",\"15:09:26\"]"),
        ["ids and enums"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Ids(System.Guid, XactJobs.Tests.Golden.Priority, System.DayOfWeek)",
            "[\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\",5,5]"),
        ["collections"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Collections(System.Int32[], System.String[], System.Collections.Generic.List`1[System.String], System.Collections.Generic.Dictionary`2[System.String, System.Int32], System.Collections.Generic.IReadOnlyList`1[System.Int64])",
            "[[1,2,3],[\"a\",\"b\"],[\"x\"],{\"one\":1,\"two\":2},[5]]"),
        ["params"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Params(System.String[])",
            "[[\"a\",\"b\",\"c\"]]"),
        ["params, none"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Params(System.String[])",
            "[[]]"),
        ["objects"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Objects(XactJobs.Tests.Golden.Address, XactJobs.Tests.Golden.Money, System.Object, XactJobs.Tests.Golden.Address)",
            "[{\"Street\":\"Main\",\"Number\":5,\"Note\":null},{\"Amount\":9.99,\"Currency\":\"EUR\"},{\"Street\":\"Boxed\",\"Number\":1,\"Note\":null},null]"),
        ["cancellation token"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "WithTokenAsync(System.Int32, System.Threading.CancellationToken)",
            "[3,null]"),
        ["job context"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "WithContextAsync(XactJobs.XactJobContext, System.Threading.CancellationToken)",
            "[null,null]"),
        ["overload, string"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Overload(System.String)",
            "[\"a\"]"),
        ["overload, int"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Overload(System.Int32)",
            "[1]"),
        ["overload, string and int"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Overload(System.String, System.Int32)",
            "[\"a\",1]"),
        ["computed arguments"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Overload(System.Int32)",
            "[42]"),
        ["captured variables"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Objects(XactJobs.Tests.Golden.Address, XactJobs.Tests.Golden.Money, System.Object, XactJobs.Tests.Golden.Address)",
            "[{\"Street\":\"Side\",\"Number\":3,\"Note\":null},{\"Amount\":3,\"Currency\":\"captured\"},\"captured\",null]"),
        ["fields and properties"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Overload(System.String, System.Int32)",
            "[\"p78\",56]"),
        ["conditional and interpolation"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Overload(System.String, System.Int32)",
            "[\"id-12\",12]"),
        ["instance"] = (
            "XactJobs.Tests.Golden.InstanceJobs, XactJobs.Tests",
            "Run()",
            "[]"),
        ["instance overload"] = (
            "XactJobs.Tests.Golden.InstanceJobs, XactJobs.Tests",
            "Run(System.String)",
            "[\"name\"]"),
        ["instance async"] = (
            "XactJobs.Tests.Golden.InstanceJobs, XactJobs.Tests",
            "RunAsync(System.Int32)",
            "[5]"),
        ["instance, Task<T>"] = (
            "XactJobs.Tests.Golden.InstanceJobs, XactJobs.Tests",
            "CountAsync()",
            "[]"),
        ["inherited method on a derived type"] = (
            "XactJobs.Tests.Golden.DerivedJobs, XactJobs.Tests",
            "Run(System.String)",
            "[\"inherited\"]"),
        ["override on a derived type"] = (
            "XactJobs.Tests.Golden.DerivedJobs, XactJobs.Tests",
            "Virtual()",
            "[]"),
        ["virtual on the base type"] = (
            "XactJobs.Tests.Golden.InstanceJobs, XactJobs.Tests",
            "Virtual()",
            "[]"),
        ["interface"] = (
            "XactJobs.Tests.Golden.IJobService, XactJobs.Tests",
            "ProcessAsync(System.Int32, System.Threading.CancellationToken)",
            "[9,null]"),
        ["nested type"] = (
            "XactJobs.Tests.Golden.Outer+Nested, XactJobs.Tests",
            "Run(System.Int32)",
            "[1]"),
        ["generic type"] = (
            "XactJobs.Tests.Golden.GenericHolder`1[[System.String, System.Private.CoreLib]], XactJobs.Tests",
            "Run(System.Int32)",
            "[1]"),
        ["generic instance type"] = (
            "XactJobs.Tests.Golden.GenericInstance`1[[System.Int32, System.Private.CoreLib]], XactJobs.Tests",
            "Run(System.Int32)",
            "[4]"),
        ["framework method"] = (
            "System.Console, System.Console",
            "WriteLine(System.String)",
            "[\"hello\"]"),
        ["internal type"] = (
            "XactJobs.Tests.Golden.InternalJobs, XactJobs.Tests",
            "Run()",
            "[]"),
        ["queue"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "NoArgs()",
            "[]"),
        ["scheduled"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Overload(System.Int32)",
            "[2]"),
        ["batch job"] = (
            "XactJobs.Tests.Golden.StaticJobs, XactJobs.Tests",
            "Overload(System.Int32)",
            "[3]"),
        ["continuation"] = (
            "XactJobs.Tests.Golden.InstanceJobs, XactJobs.Tests",
            "RunAsync(System.Int32)",
            "[6]"),
        ["periodic"] = (
            "XactJobs.Tests.Golden.InstanceJobs, XactJobs.Tests",
            "RunAsync(System.Int32)",
            "[7]"),
    };

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var (name, _) in Cases) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Job_IsStoredAsTheseStrings(string name)
    {
        var (typeName, methodName, methodArgs) = Cases.Single(c => c.Name == name).Enqueue(_db) switch
        {
            XactJobBase job => (job.TypeName, job.MethodName, job.MethodArgs),
            XactJobPeriodic periodic => (periodic.TypeName, periodic.MethodName, periodic.MethodArgs),
            var other => throw new InvalidOperationException($"Not a job: {other}"),
        };


        Assert.True(Expected.ContainsKey(name), $"No expected strings for '{name}'");

        var expected = Expected[name];

        Assert.Equal(expected.TypeName, typeName);
        Assert.Equal(expected.MethodName, methodName);
        Assert.Equal(expected.MethodArgs, methodArgs);
    }

    [Fact]
    public void EveryCase_HasExpectedStrings()
    {
        Assert.Equal(Cases.Select(c => c.Name).Order(), Expected.Keys.Order());
    }
}
