using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using XactJobs.Internal.SqlDialects;
using XactJobs.Tests.Postgres;

namespace XactJobs.Tests.Databases;

// The class names start with the database, so a filter like "XactJobs.Tests.Databases.Oracle" runs all the suites of one database.
// Only PostgreSQL and SQL Server have schemas for XactJobs' tables: elsewhere each app has its own database (on Oracle, its own user).

[Collection(SqlServerCollection.Name)]
public sealed class SqlServerSchemaTests(SqlServerFixture fixture) : SchemaIntegrationTests(fixture);

[Collection(Postgres16Collection.Name)]
public sealed class Postgres16SchemaTests(Postgres16Fixture fixture) : SchemaIntegrationTests(fixture);

[Collection(Postgres17Collection.Name)]
public sealed class Postgres17SchemaTests(Postgres17Fixture fixture) : SchemaIntegrationTests(fixture);

[Collection(Postgres18Collection.Name)]
public sealed class Postgres18SchemaTests(Postgres18Fixture fixture) : SchemaIntegrationTests(fixture);

/// <summary>
/// Two apps in one database, each with XactJobs' tables in its own schema: the fixture's default tables, and app B's, created from the
/// install script under app B's schema. App B's workers run only app B's jobs, and its periodic jobs go to its own tables.
/// </summary>
public abstract class SchemaIntegrationTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    private bool IsPostgres => fixture.Dialect is PostgreSqlDialect;

    private string AppBSchema => IsPostgres ? "app_b" : "AppB";

    // the jobs' names, so that the static log tells this test's runs apart
    private readonly string _defaultApp = $"default app {Guid.NewGuid():N}";
    private readonly string _appB = $"app B {Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        if (DatabaseFixture.IsDockerAvailable)
        {
            await fixture.PrepareForTestAsync();
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [DockerFact]
    public async Task AppsInTheirOwnSchemas_RunOnlyTheirOwnJobs()
    {
        await using (var db = CreateDefaultDb())
        {
            var installScript = await File.ReadAllTextAsync(InstallScriptIntegrationTests.GetInstallScript(IsPostgres ? "postgresql" : "sqlserver"));

            await db.Database.ExecuteSqlRawAsync(installScript.Replace(IsPostgres ? "xact_jobs" : "XactJobs", AppBSchema));
        }

        try
        {
            long defaultJobId;
            long appBJobId;

            var defaultApp = _defaultApp;
            var appB = _appB;

            await using (var db = CreateDefaultDb())
            {
                db.JobEnqueue(() => SchemaJobs.Run(defaultApp));
                await db.SaveChangesAsync();
                defaultJobId = (await db.Jobs.AsNoTracking().SingleAsync()).Id;
            }

            await using (var db = CreateAppBDb())
            {
                db.JobEnqueue(() => SchemaJobs.Run(appB));
                await db.SaveChangesAsync();
                appBJobId = (await db.Jobs.AsNoTracking().SingleAsync()).Id;
            }

            using var host = CreateAppBHost();

            await host.StartAsync();

            await WaitForAsync(async () =>
            {
                await using var db = CreateAppBDb();
                return await db.JobHistory.AnyAsync(h => h.Id == appBJobId && h.Status == XactJobStatus.Completed);
            }, "app B's job to complete");

            await WaitForAsync(async () =>
            {
                await using var db = CreateAppBDb();
                return await db.PeriodicJobs.AnyAsync(p => p.Id == "app_b_periodic");
            }, "app B's periodic job to be added");

            // more than a polling interval: app B's worker would have claimed it by now
            await Task.Delay(TimeSpan.FromSeconds(2));

            await host.StopAsync();

            Assert.Contains(_appB, SchemaJobs.Runs);
            Assert.DoesNotContain(_defaultApp, SchemaJobs.Runs);

            await using (var db = CreateDefaultDb())
            {
                var defaultJob = await db.Jobs.AsNoTracking().SingleAsync();

                Assert.Equal(defaultJobId, defaultJob.Id);
                Assert.Null(defaultJob.Leaser);
                Assert.Empty(await db.JobHistory.AsNoTracking().ToListAsync());
                Assert.Empty(await db.PeriodicJobs.AsNoTracking().ToListAsync());
            }
        }
        finally
        {
            await DropAppBTablesAsync();
        }
    }

    private IHost CreateAppBHost()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<AppBDbContext>(options => fixture.Configure(options, retryOnFailure: false));

        builder.Services.AddXactJobs<AppBDbContext>(options => options
            .WithWorkerCount(1)
            .WithPollingInterval(1)
            .WithShutdownTimeout(0)
            .WithPeriodicJob(() => SchemaJobs.Run("app B, periodic"), "app_b_periodic", "0 0 0 1 1 *"));

        return builder.Build();
    }

    private async Task DropAppBTablesAsync()
    {
        await using var db = CreateDefaultDb();

        if (IsPostgres)
        {
            // each test has a database of its own, but the schema is dropped all the same
            var dropSchema = "DROP SCHEMA IF EXISTS " + AppBSchema + " CASCADE";

            await db.Database.ExecuteSqlRawAsync(dropSchema);
        }
        else
        {
            // the schema is a constant of this test
            var dropTables = string.Join(", ", new[] { "Job", "JobHistory", "JobPeriodic" }.Select(t => "[" + AppBSchema + "].[" + t + "]"));
            var dropTablesSql = "DROP TABLE IF EXISTS " + dropTables;
            var dropSchemaSql = "IF SCHEMA_ID(N'" + AppBSchema + "') IS NOT NULL DROP SCHEMA [" + AppBSchema + "]";

            await db.Database.ExecuteSqlRawAsync(dropTablesSql);
            await db.Database.ExecuteSqlRawAsync(dropSchemaSql);
        }
    }

    private TestDbContext CreateDefaultDb()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>();

        fixture.Configure(options, retryOnFailure: false);

        return new TestDbContext(options.Options);
    }

    private AppBDbContext CreateAppBDb()
    {
        var options = new DbContextOptionsBuilder<AppBDbContext>();

        fixture.Configure(options, retryOnFailure: false);

        return new AppBDbContext(options.Options);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, string description)
    {
        var deadline = DateTime.UtcNow + WaitTimeout;

        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, $"Timed out waiting for {description}");

            await Task.Delay(50);
        }
    }

    public static class SchemaJobs
    {
        private static readonly ConcurrentQueue<string> _runs = new();

        public static IReadOnlyCollection<string> Runs => _runs;

        public static void Run(string app) => _runs.Enqueue(app);
    }
}

/// <summary>
/// App B's context: XactJobs' tables in its own schema. EF caches a model per context type, so each app has a context type of its own.
/// </summary>
public sealed class AppBDbContext(DbContextOptions<AppBDbContext> options) : DbContext(options)
{
    public DbSet<XactJob> Jobs => Set<XactJob>();
    public DbSet<XactJobHistory> JobHistory => Set<XactJobHistory>();
    public DbSet<XactJobPeriodic> PeriodicJobs => Set<XactJobPeriodic>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var schema = Database.ProviderName!.Contains("Npgsql") ? "app_b" : "AppB";

        modelBuilder.ApplyXactJobsConfigurations(Database.ProviderName, includeInMigrations: true, schema: schema);
    }
}
