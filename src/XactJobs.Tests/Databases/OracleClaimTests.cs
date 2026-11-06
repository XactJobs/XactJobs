using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;
using XactJobs.Internal.SqlDialects;

namespace XactJobs.Tests.Databases;

/// <summary>
/// Oracle's claims under concurrency: workers that poll at once must each get jobs from a backlog, oldest first,
/// and a claim must lock only the rows it claims.
/// </summary>
[Collection(OracleCollection.Name)]
public sealed class OracleClaimTests(OracleFixture fixture) : IAsyncLifetime
{
    private const string Queue = "claim-test";
    private const int MaxJobs = 5;

    private static readonly OracleDialect Dialect = new();

    public async Task InitializeAsync()
    {
        if (DatabaseFixture.IsDockerAvailable)
        {
            await fixture.PrepareForTestAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// A claims in a transaction that stays open, then B claims, and C tries to lock every row B didn't get, without waiting.
    /// The pools are cleared first: on a new connection, ODP.NET prefetches far more rows than asked for on a statement's first
    /// execution, so a claim that limits the rows on the client would lock most of the backlog.
    /// </summary>
    [DockerFact]
    public async Task ConcurrentClaims_UnderABacklog_EachGetDifferentJobs_AndLockOnlyThose()
    {
        var backlog = await AddDueJobsAsync(20);

        OracleConnection.ClearAllPools();

        await using var dbA = CreateDb();

        await using var transactionA = await dbA.Database.BeginTransactionAsync();

        var claimedByA = await Dialect.ClaimJobsAsync(dbA, Queue, MaxJobs, Guid.NewGuid(), 60, CancellationToken.None);

        List<XactJob> claimedByB;

        await using (var dbB = CreateDb())
        {
            claimedByB = await Dialect.ClaimJobsAsync(dbB, Queue, MaxJobs, Guid.NewGuid(), 60, CancellationToken.None);
        }

        // B has committed: only A's rows are locked now
        var (lockedByC, lockedByOthers) = await TryLockWithoutWaitingAsync(backlog.Where(j => !claimedByB.Any(b => b.Id == j.Id)));

        await transactionA.CommitAsync();

        Assert.Equal(MaxJobs, claimedByA.Count);
        Assert.Equal(MaxJobs, claimedByB.Count);

        // oldest first: A got the 5 oldest, B the next 5
        Assert.Equal(backlog.Take(MaxJobs).Select(j => j.Id), claimedByA.Select(j => j.Id));
        Assert.Equal(backlog.Skip(MaxJobs).Take(MaxJobs).Select(j => j.Id), claimedByB.Select(j => j.Id));

        // A locked its own 5 rows, and no others
        Assert.Equal(claimedByA.Select(j => j.Id).Order(), lockedByOthers.Order());
        Assert.Equal(backlog.Skip(2 * MaxJobs).Select(j => j.Id).Order(), lockedByC.Order());
    }

    [DockerFact]
    public async Task ConcurrentContinuationClaims_EachGetDifferentContinuations()
    {
        var continuations = await AddClaimableContinuationsAsync(9);

        OracleConnection.ClearAllPools();

        await using var dbA = CreateDb();

        await using var transactionA = await dbA.Database.BeginTransactionAsync();

        var claimedByA = await Dialect.ClaimContinuationsAsync(dbA, Queue, 3, Guid.NewGuid(), 60, CancellationToken.None);

        List<XactJob> claimedByB;

        await using (var dbB = CreateDb())
        {
            claimedByB = await Dialect.ClaimContinuationsAsync(dbB, Queue, 3, Guid.NewGuid(), 60, CancellationToken.None);
        }

        var (lockedByC, lockedByOthers) = await TryLockWithoutWaitingAsync(continuations.Where(j => !claimedByB.Any(b => b.Id == j.Id)));

        await transactionA.CommitAsync();

        Assert.Equal(continuations.Take(3).Select(j => j.Id), claimedByA.Select(j => j.Id));
        Assert.Equal(continuations.Skip(3).Take(3).Select(j => j.Id), claimedByB.Select(j => j.Id));
        Assert.Equal(claimedByA.Select(j => j.Id).Order(), lockedByOthers.Order());
        Assert.Equal(continuations.Skip(6).Select(j => j.Id).Order(), lockedByC.Order());
    }

    [DockerFact]
    public async Task Claim_LeasesTheJobs_AndReturnsThemAsTheyWereLocked()
    {
        await AddDueJobsAsync(3);

        await using var db = CreateDb();

        var claimed = await Dialect.ClaimJobsAsync(db, Queue, 2, Guid.NewGuid(), 60, CancellationToken.None);

        Assert.Equal(2, claimed.Count);
        Assert.All(claimed, job => Assert.Null(job.LeasedUntil));

        await using var check = CreateDb();

        var leased = await check.Jobs.AsNoTracking().Where(j => j.Queue == Queue && j.LeasedUntil > DateTime.UtcNow).Select(j => j.Id).OrderBy(id => id).ToListAsync();

        Assert.Equal(claimed.Select(j => j.Id).Order(), leased);
    }

    [DockerFact]
    public async Task ClaimJobs_ReturnsTheOldestFirst()
    {
        // inserted out of order, so that the ids and the table's order differ from the scheduled order
        var now = DateTime.UtcNow;

        int[] minutesAgo = [3, 9, 1, 7, 5, 2, 8, 4, 6, 10];

        await using (var db = CreateDb())
        {
            foreach (var minutes in minutesAgo)
            {
                db.Jobs.Add(NewJob(now.AddMinutes(-minutes)));

                await db.SaveChangesAsync();
            }
        }

        List<long> oldestFirst;

        await using (var db = CreateDb())
        {
            oldestFirst = await db.Jobs.Where(j => j.Queue == Queue).OrderBy(j => j.ScheduledAt).Select(j => j.Id).ToListAsync();
        }

        Assert.NotEqual(oldestFirst.Order(), oldestFirst); // the ids aren't in scheduled order

        await using var claimDb = CreateDb();

        var claimed = await Dialect.ClaimJobsAsync(claimDb, Queue, 4, Guid.NewGuid(), 60, CancellationToken.None);

        Assert.Equal(oldestFirst.Take(4), claimed.Select(j => j.Id));
    }

    [DockerFact]
    public async Task ClaimContinuations_ReturnsThemInIdOrder()
    {
        var continuations = await AddClaimableContinuationsAsync(6);

        await using var claimDb = CreateDb();

        var claimed = await Dialect.ClaimContinuationsAsync(claimDb, Queue, 4, Guid.NewGuid(), 60, CancellationToken.None);

        Assert.Equal(continuations.Take(4).Select(j => j.Id), claimed.Select(j => j.Id));
    }

    /// <summary>
    /// Adds continuations that wait for batches with no jobs left, and returns them as stored, in id order.
    /// </summary>
    private async Task<List<XactJob>> AddClaimableContinuationsAsync(int count)
    {
        await using var db = CreateDb();

        for (var i = 0; i < count; i++)
        {
            db.Jobs.Add(new XactJob(0, XactJobBase.AwaitingBatchScheduledAt, "T", "M()", "[]", Queue)
            {
                AwaitsBatchId = Guid.NewGuid(),
                ContinuationCondition = XactJobContinuation.OnSuccess
            });
        }

        await db.SaveChangesAsync();

        return await db.Jobs.AsNoTracking().Where(j => j.Queue == Queue).OrderBy(j => j.Id).ToListAsync();
    }

    /// <summary>
    /// Adds due jobs a second apart, and returns them as stored, oldest first.
    /// </summary>
    private async Task<List<XactJob>> AddDueJobsAsync(int count)
    {
        await using var db = CreateDb();

        var oldest = DateTime.UtcNow.AddHours(-1);

        for (var i = 0; i < count; i++)
        {
            db.Jobs.Add(NewJob(oldest.AddSeconds(i)));
        }

        await db.SaveChangesAsync();

        return await db.Jobs.AsNoTracking().Where(j => j.Queue == Queue).OrderBy(j => j.ScheduledAt).ToListAsync();
    }

    /// <summary>
    /// Locks each row by primary key with NOWAIT, in one transaction that's rolled back. Returns the ids it locked,
    /// and the ids that another transaction had locked.
    /// </summary>
    private async Task<(List<long> Locked, List<long> LockedByOthers)> TryLockWithoutWaitingAsync(IEnumerable<XactJob> jobs)
    {
        await using var db = CreateDb();

        await using var transaction = await db.Database.BeginTransactionAsync();

        List<long> locked = [];
        List<long> lockedByOthers = [];

        foreach (var job in jobs)
        {
            try
            {
                await db.Database
                    .SqlQueryRaw<long>("SELECT ID AS \"Value\" FROM XACT_JOBS_JOB WHERE QUEUE = {0} AND SCHEDULED_AT = {1} AND ID = {2} FOR UPDATE NOWAIT", job.Queue, job.ScheduledAt, job.Id)
                    .ToListAsync();

                locked.Add(job.Id);
            }
            catch (Exception ex) when (ex.Message.Contains("ORA-00054"))
            {
                lockedByOthers.Add(job.Id);
            }
        }

        await transaction.RollbackAsync();

        return (locked, lockedByOthers);
    }

    private static XactJob NewJob(DateTime scheduledAt) => new(0, scheduledAt, "T", "M()", "[]", Queue);

    private TestDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>();

        fixture.Configure(options, retryOnFailure: false);

        return new TestDbContext(options.Options);
    }
}
