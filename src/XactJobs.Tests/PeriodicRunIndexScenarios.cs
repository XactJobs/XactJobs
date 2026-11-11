using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.Tests;

/// <summary>
/// The unique index of the job table on the periodic job and its version: one pending run per version. The scenarios run on every
/// database: see <c>Databases/PeriodicRunIndexIntegrationTests</c> and <see cref="SqlitePeriodicRunIndexTests"/>.
/// </summary>
public sealed class PeriodicRunIndexScenarios(Action<DbContextOptionsBuilder> configure)
{
    private const string Hourly = "0 0 * * * *";

    /// <summary>
    /// A second run of a version is refused, and <see cref="DbExceptionExtensions.IsUniqueKeyViolation"/> knows the provider's
    /// error. A run of another version is not, nor are jobs of no periodic job.
    /// </summary>
    public async Task SecondRunOfAVersion_IsRefused()
    {
        await EnsurePeriodicAsync("unique");

        await using (var db = CreateDb())
        {
            var periodicJob = await db.PeriodicJobs.SingleAsync(p => p.Id == "unique");

            db.Jobs.Add(RunOf(periodicJob, periodicJob.Version));

            var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

            Assert.True(ex.IsUniqueKeyViolation(), ex.ToString());
        }

        await using (var db = CreateDb())
        {
            var periodicJob = await db.PeriodicJobs.SingleAsync(p => p.Id == "unique");

            db.Jobs.Add(RunOf(periodicJob, periodicJob.Version + 1));

            var scheduledAt = DateTime.UtcNow.AddHours(1);

            db.Jobs.Add(new XactJob(0, scheduledAt, "T", "M()", "[]", QueueNames.Default));
            db.Jobs.Add(new XactJob(0, scheduledAt, "T", "M()", "[]", QueueNames.Default));

            await db.SaveChangesAsync();

            Assert.Equal(2, await db.Jobs.CountAsync(j => j.PeriodicJobId == "unique"));
        }
    }

    /// <summary>
    /// The dashboard's Trigger of a periodic job without a pending run, and its Resume, when a run is added at the same time (by
    /// another request, the app, or the scheduler): the save is refused, and the dashboard answers with a conflict.
    /// </summary>
    public async Task Dashboard_TriggerAndResume_WhenARunIsAddedAtTheSameTime_AreConflicts()
    {
        await EnsurePeriodicAsync("trigger");
        await EnsurePeriodicAsync("resume");

        await using (var db = CreateDb())
        {
            // the chain of runs got lost
            db.Jobs.RemoveRange(await db.Jobs.Where(j => j.PeriodicJobId == "trigger").ToListAsync());

            await db.JobSetPeriodicActiveAsync("resume", false, CancellationToken.None);

            await db.SaveChangesAsync();
        }

        await AssertConflictAsync("trigger", service => service.TriggerPeriodicJobAsync("trigger"));
        await AssertConflictAsync("resume", service => service.SetPeriodicJobActiveAsync("resume", true));
    }

    private async Task AssertConflictAsync(string id, Func<XactJobsUIService<TestDbContext>, Task<ActionResultDto>> action)
    {
        var addRun = new AddRunBeforeSave(this, id);

        await using (var db = CreateDb(addRun))
        {
            var result = await action(new XactJobsUIService<TestDbContext>(db));

            Assert.True(addRun.IsAdded);
            Assert.Equal(ActionStatus.Conflict, result.Status);
        }

        await using (var db = CreateDb())
        {
            Assert.Equal(1, await db.Jobs.CountAsync(j => j.PeriodicJobId == id));
        }
    }

    private async Task EnsurePeriodicAsync(string id)
    {
        await using var db = CreateDb();

        db.JobEnsurePeriodic<IndexJob>(x => x.Run(), id, Hourly);

        await db.SaveChangesAsync();
    }

    private static XactJob RunOf(XactJobPeriodic periodicJob, int version) =>
        new(0,
            DateTime.UtcNow.AddHours(1),
            periodicJob.TypeName,
            periodicJob.MethodName,
            periodicJob.MethodArgs,
            periodicJob.Queue,
            periodicJob.Id,
            periodicJob.CronExpression,
            version);

    private TestDbContext CreateDb(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>();

        configure(options);

        if (interceptors.Length > 0)
        {
            options.AddInterceptors(interceptors);
        }

        return new TestDbContext(options.Options);
    }

    /// <summary>
    /// Adds a run of the periodic job from another context, committed, right before the first save of the dashboard's context.
    /// </summary>
    private sealed class AddRunBeforeSave(PeriodicRunIndexScenarios scenarios, string id) : SaveChangesInterceptor
    {
        public bool IsAdded { get; private set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!IsAdded)
            {
                IsAdded = true;

                await using var other = scenarios.CreateDb();

                var periodicJob = await other.PeriodicJobs.SingleAsync(p => p.Id == id, cancellationToken);

                other.Jobs.Add(RunOf(periodicJob, periodicJob.Version));

                await other.SaveChangesAsync(cancellationToken);
            }

            return result;
        }
    }

    public sealed class IndexJob
    {
        public void Run() { }
    }
}
