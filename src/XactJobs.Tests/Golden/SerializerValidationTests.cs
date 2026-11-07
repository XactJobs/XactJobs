using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace XactJobs.Tests.Golden;

/// <summary>
/// Which expressions can't be jobs, and the exception they're rejected with when they're enqueued. And a few that look odd but are
/// accepted. The messages aren't part of the contract, the exception types are.
/// </summary>
public class SerializerValidationTests : IDisposable
{
    private readonly TestDbContext _db;

    public SerializerValidationTests()
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

    private static void PrivateJob() { }

    private static readonly Action SomeAction = () => { };

    public static readonly (string Name, Action<TestDbContext> Enqueue)[] Cases =
    [
        ("generic method", db => db.JobEnqueue<InvalidJobs>(x => x.Generic(1))),
        ("delegate parameter", db => db.JobEnqueue<InvalidJobs>(x => x.WithDelegate(SomeAction))),
        ("func parameter", db => db.JobEnqueue<InvalidJobs>(x => x.WithFunc(() => 1))),
        ("async void", db => db.JobEnqueue<InvalidJobs>(x => x.AsyncVoid())),
        ("internal method", db => db.JobEnqueue<InvalidJobs>(x => x.NotPublic())),
        ("private static method", db => db.JobEnqueue(() => PrivateJob())),
        ("not a method call", db => db.JobEnqueue(() => SomeAction())),
        ("method of another instance", db => db.JobEnqueue<InstanceJobs>(x => new InstanceJobs().Run())),
        ("instance method from a static lambda", db => db.JobEnqueue(() => new InstanceJobs().Run())),
        ("periodic generic method", db => db.JobEnsurePeriodic<InvalidJobs>(x => x.Generic(1), "golden_invalid", Cron.Daily())),
        ("local scheduled time", db => db.JobScheduleAt(() => StaticJobs.NoArgs(), new DateTime(2026, 3, 14, 15, 9, 26, DateTimeKind.Local))),
        ("unspecified scheduled time", db => db.JobScheduleAt(() => StaticJobs.NoArgs(), new DateTime(2026, 3, 14, 15, 9, 26, DateTimeKind.Unspecified))),
    ];

    /// <summary>The exception each case is rejected with, or null if it's accepted.</summary>
    public static readonly Dictionary<string, Type?> Expected = new()
    {
        ["generic method"] = null, // a known gap: accepted here, and it fails when it runs (a job can't be a generic method)
        ["delegate parameter"] = typeof(System.NotSupportedException),
        ["func parameter"] = typeof(System.NotSupportedException),
        ["async void"] = typeof(System.NotSupportedException),
        ["internal method"] = typeof(System.NotSupportedException),
        ["private static method"] = typeof(System.NotSupportedException),
        ["not a method call"] = typeof(System.ArgumentException),
        ["method of another instance"] = null, // stored as InstanceJobs.Run(): the worker creates its own instance
        ["instance method from a static lambda"] = null, // the same
        ["periodic generic method"] = null, // the same gap
        ["local scheduled time"] = typeof(System.ArgumentException),
        ["unspecified scheduled time"] = typeof(System.ArgumentException),
    };

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var (name, _) in Cases) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void Expression_IsRejectedOrAccepted(string name)
    {
        var exception = Record.Exception(() => Cases.Single(c => c.Name == name).Enqueue(_db));


        Assert.True(Expected.ContainsKey(name), $"No expected outcome for '{name}'");

        var expected = Expected[name];

        if (expected == null)
        {
            Assert.Null(exception);
        }
        else
        {
            Assert.NotNull(exception);
            Assert.IsType(expected, exception);
        }
    }

    [Fact]
    public void EveryCase_HasAnExpectedOutcome()
    {
        Assert.Equal(Cases.Select(c => c.Name).Order(), Expected.Keys.Order());
    }
}
