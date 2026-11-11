using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using MySql.Data.MySqlClient;
using Npgsql;
using XactJobs.Internal;

namespace XactJobs.Benchmarks.Drain;

/// <summary>
/// Drains a backlog of jobs that do nothing, on SQLite in memory, twice:
/// <list type="bullet">
/// <item>with XactJobs: a host with one worker;</item>
/// <item>with plain EF, the least database work for the same result: the same claim, then for each job its delete and its history row,
/// saved together. Like the worker, each of the slots claims a job when it's free.</item>
/// </list>
/// The difference per job is the cost of XactJobs' own code: scopes, the runner's bookkeeping, running the job, the save check.
/// The database work is in both. SQLite has a single writer, so this measures neither the database nor concurrency: with more than
/// one job at a time, the claims and the saves wait for each other.
/// <para>
/// Both are measured from the first completed job: a worker's first poll waits for its polling interval, which isn't part of the drain.
/// </para>
/// </summary>
public static class DrainBenchmark
{
    private const int LeaseDurationInSeconds = 120;

    public static async Task<int> RunAsync(string[] args)
    {
        var jobs = GetOption(args, "--jobs", 5000);
        var runs = GetOption(args, "--runs", 5);
        var settings = new DrainSettings(
            Workers: GetOption(args, "--workers", 1),
            Parallelism: GetOption(args, "--parallelism", 1),
            BatchSize: args.Contains("--batch-size") ? GetOption(args, "--batch-size", 1) : null,
            Postgres: GetString(args, "--postgres"),
            SqlServer: GetString(args, "--sqlserver"),
            MySql: GetString(args, "--mysql"),
            Log: args.Contains("--log"),
            ContextPool: args.Contains("--context-pool"));
        var listSql = args.Contains("--sql");
        var only = GetOnly(args);

        if (jobs < 2) throw new ArgumentException("--jobs needs at least 2: the first one only starts the measurement");

        Console.WriteLine($"Drain: {jobs} jobs, {settings.Workers} worker(s) running {settings.Parallelism} at a time each" +
                          $"{(settings.BatchSize != null ? $", claiming batches of {settings.BatchSize}" : "")}" +
                          $"{(settings.ContextPool ? ", pooled contexts" : "")}, median of {runs} runs after a warm-up");
        Console.WriteLine($".NET {Environment.Version}, EF Core {GetVersion(typeof(DbContext))}, XactJobs {GetVersion(typeof(XactJob))}, " +
                          $"{(settings.Postgres != null ? "PostgreSQL" : settings.SqlServer != null ? "SQL Server" : settings.MySql != null ? "MySQL" : "SQLite in memory")}, {Environment.ProcessorCount} processors");
#if DEBUG
        Console.WriteLine("WARNING: a Debug build. Run with -c Release.");
#endif
        Console.WriteLine();

        var runXactJobs = only is null or "xactjobs";
        var runEf = only is null or "ef";

        // warm-up: JIT, EF's model and query caches. Not measured: a small backlog can be drained before all the workers poll,
        // on a fast machine, when the measurement would start
        if (runXactJobs) await DrainWithXactJobsAsync(Math.Min(jobs, 500), settings, listSql: false, measure: false);
        if (runEf) await DrainWithEfAsync(Math.Min(jobs, 500), settings, listSql: false, measure: false);

        List<Measurement> xactJobs = [];
        List<Measurement> ef = [];

        for (var run = 0; run < runs; run++)
        {
            // alternated, so that a slower moment of the machine doesn't hit only one of them
            if (runXactJobs) xactJobs.Add(await DrainWithXactJobsAsync(jobs, settings, listSql && run == 0));
            if (runEf) ef.Add(await DrainWithEfAsync(jobs, settings, listSql && run == 0));

            Console.WriteLine($"  run {run + 1}:" +
                              (runXactJobs ? $" XactJobs {xactJobs[^1].Wall.TotalMilliseconds:F0} ms" : "") +
                              (runEf ? $" plain EF {ef[^1].Wall.TotalMilliseconds:F0} ms" : ""));
        }

        Console.WriteLine();
        Console.WriteLine($"{"per job",-20}{"jobs/s",10}{"wall µs",10}{"CPU µs",10}{"KB",9}{"SQL",8}{"SQL µs",10}");

        Measurement? x = runXactJobs ? Measurement.Median(xactJobs) : null;
        Measurement? e = runEf ? Measurement.Median(ef) : null;

        if (x != null) Print("XactJobs", x.Value);
        if (e != null) Print("plain EF", e.Value);

        if (x != null && e != null)
        {
            Console.WriteLine($"{"XactJobs overhead",-20}{"",10}{x.Value.WallPerJob - e.Value.WallPerJob,10:F1}{x.Value.CpuPerJob - e.Value.CpuPerJob,10:F1}" +
                              $"{x.Value.KBPerJob - e.Value.KBPerJob,9:F2}{x.Value.CommandsPerJob - e.Value.CommandsPerJob,8:F2}{x.Value.SqlPerJob - e.Value.SqlPerJob,10:F1}");

            // as the runner: a batch at or below the parallelism is no batch
            var batch = settings.BatchSize is > 0 and var size ? Math.Max(size, settings.Parallelism) : 2 * settings.Parallelism;

            if (batch > settings.Parallelism)
            {
                Console.WriteLine();
                Console.WriteLine($"The XactJobs workers pick up batches of {batch} jobs{(settings.BatchSize == null ? " (by default twice the parallelism)" : "")}, " +
                                  "plain EF one job at a time, so the overhead row compares different database work.");
                Console.WriteLine($"For XactJobs' own cost, run with --batch-size {settings.Parallelism}, where both pick up only the jobs they can start.");
            }
        }

        return 0;
    }

    private const int PollingIntervalInSeconds = 1;

    /// <summary>Drains the backlog with XactJobs. Without <paramref name="measure"/> (the warm-up), the result is empty.</summary>
    private static async Task<Measurement> DrainWithXactJobsAsync(int jobs, DrainSettings settings, bool listSql, bool measure = true)
    {
        await using var database = await BenchDatabase.CreateAsync(settings, jobs);

        var completed = new CompletedJobCounter(jobs);
        var commands = new CommandCounter();

        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        if (settings.Log)
        {
            // XactJobs' warnings and errors, e.g. a job that failed
            builder.Logging.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Warning);
        }

        AddContext(builder.Services, settings, options => database.Configure(options).AddInterceptors(completed, commands));

        // the drain starts and stops the host itself. The console lifetime would take Ctrl+C: it would stop the workers and keep
        // the process alive, while the drain waits for jobs that no longer run. Without it, Ctrl+C ends the process.
        builder.Services.AddSingleton<IHostLifetime, NoConsoleLifetime>();

        builder.Services.AddXactJobs<BenchDbContext>(options =>
        {
            options
                .WithWorkerCount(settings.Workers)
                .WithMaxDegreeOfParallelism(settings.Parallelism)
                .WithPollingInterval(PollingIntervalInSeconds)
                .WithShutdownTimeout(0);

            if (settings.BatchSize != null)
            {
                options.WithBatchSize(settings.BatchSize.Value);
            }
        });

        using var host = builder.Build();

        await host.StartAsync();

        // the last worker starts after its share of the polling interval, and its first poll waits for the interval:
        // measured once all of them poll
        var allPolling = Task.Delay(TimeSpan.FromSeconds(PollingIntervalInSeconds) * (1 + (settings.Workers - 1.0) / settings.Workers) + TimeSpan.FromMilliseconds(200));

        Measurement measurement = default;

        if (measure)
        {
            measurement = await Measurement.MeasureAsync(completed, commands, allPolling);
        }
        else
        {
            await completed.AllCompleted;
        }

        await host.StopAsync();

        await database.AssertDrainedAsync(jobs);

        if (completed.FailedAttempts > 0) Console.WriteLine($"  XactJobs: {completed.FailedAttempts} failed attempt(s), retried (--log shows why)");
        if (completed.PeriodicRuns > 0) Console.WriteLine($"  XactJobs: its own periodic job ran {completed.PeriodicRuns} time(s) during the drain (not counted)");

        if (listSql) commands.Print("XactJobs", measurement.Jobs);

        return measurement;
    }

    /// <summary>A host lifetime that does nothing: the drain starts and stops the host, and Ctrl+C isn't handled.</summary>
    private sealed class NoConsoleLifetime : IHostLifetime
    {
        public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Drains the backlog with plain EF. Without <paramref name="measure"/> (the warm-up), the result is empty.</summary>
    private static async Task<Measurement> DrainWithEfAsync(int jobs, DrainSettings settings, bool listSql, bool measure = true)
    {
        await using var database = await BenchDatabase.CreateAsync(settings, jobs);

        var completed = new CompletedJobCounter(jobs);
        var commands = new CommandCounter();

        await using var services = AddContext(new ServiceCollection(), settings, options => database.Configure(options).AddInterceptors(completed, commands))
            .BuildServiceProvider();

        var leaser = Guid.NewGuid();

        // as many slots as the workers have in all
        var slots = Task.WhenAll(Enumerable.Range(0, settings.Workers * settings.Parallelism).Select(_ => Task.Run(() => RunSlotAsync(services, leaser))));

        Measurement measurement = default;

        if (measure)
        {
            measurement = await Measurement.MeasureAsync(completed, commands);
        }
        else
        {
            await completed.AllCompleted;
        }

        await slots;

        await database.AssertDrainedAsync(jobs);

        if (completed.FailedAttempts > 0) Console.WriteLine($"  plain EF: {completed.FailedAttempts} failed attempt(s)");

        if (listSql) commands.Print("plain EF", measurement.Jobs);

        return measurement;
    }

    /// <summary>
    /// Claims a job, completes it, and claims the next one, until there's none left.
    /// </summary>
    private static async Task RunSlotAsync(IServiceProvider services, Guid leaser)
    {
        while (true)
        {
            List<XactJob> claimed;

            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BenchDbContext>();

                // the worker's claim, with its locking (FOR UPDATE SKIP LOCKED on PostgreSQL and MySQL, READPAST on SQL Server)
                var dialect = db.Database.ProviderName.ToSqlDialect();

                claimed = await dialect.ClaimJobsAsync(db, QueueNames.Default, 1, leaser, LeaseDurationInSeconds, CancellationToken.None);
            }

            if (claimed.Count == 0) return;

            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BenchDbContext>();

                db.Database.AutoTransactionBehavior = AutoTransactionBehavior.Always;

                var job = claimed[0];

                db.Attach(job);
                db.JobHistory.Add(XactJobHistory.CreateFromJob(job, DateTime.UtcNow, XactJobStatus.Completed, job.ErrorCount, null));
                db.Jobs.Remove(job);

                await db.SaveChangesAsync();
            }
        }
    }

    private static void Print(string name, Measurement m)
    {
        Console.WriteLine($"{name,-20}{m.Jobs / m.Wall.TotalSeconds,10:F0}{m.WallPerJob,10:F1}{m.CpuPerJob,10:F1}{m.KBPerJob,9:F2}{m.CommandsPerJob,8:F2}{m.SqlPerJob,10:F1}");
    }

    private static string GetVersion(Type type) =>
        type.Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "?";

    private static string? GetOnly(string[] args)
    {
        var index = Array.IndexOf(args, "--only");

        if (index < 0) return null;

        if (index + 1 >= args.Length || args[index + 1] is not ("xactjobs" or "ef"))
        {
            throw new ArgumentException("--only needs xactjobs or ef");
        }

        return args[index + 1];
    }

    private static string? GetString(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);

        if (index < 0) return null;

        if (index + 1 >= args.Length) throw new ArgumentException($"{name} needs a value");

        return args[index + 1];
    }

    // the plain EF baseline doesn't batch: each of its slots claims one job
    private sealed record DrainSettings(int Workers, int Parallelism, int? BatchSize, string? Postgres, string? SqlServer, string? MySql, bool Log, bool ContextPool);

    /// <summary>
    /// The context, created for each scope, or with <c>--context-pool</c> reused from a pool (AddDbContextPool), for both drains.
    /// </summary>
    private static IServiceCollection AddContext(IServiceCollection services, DrainSettings settings, Action<DbContextOptionsBuilder> configure)
    {
        return settings.ContextPool
            ? services.AddDbContextPool<BenchDbContext>(configure)
            : services.AddDbContext<BenchDbContext>(configure);
    }

    private static int GetOption(string[] args, string name, int defaultValue)
    {
        var index = Array.IndexOf(args, name);

        if (index < 0) return defaultValue;

        if (index + 1 >= args.Length || !int.TryParse(args[index + 1], CultureInfo.InvariantCulture, out var value) || value < 1)
        {
            throw new ArgumentException($"{name} needs a positive number");
        }

        return value;
    }

    /// <summary>
    /// Counts the saves that complete a job (the ones with a history row), so the drain's progress is known without polling the database.
    /// </summary>
    private sealed class CompletedJobCounter(int expected) : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _firstCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _allCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _count;
        private int _failedAttempts;
        private int _periodicRuns;

        public int Expected => expected;

        public int Count => Volatile.Read(ref _count);

        /// <summary>
        /// History rows of the other statuses, e.g. a failed attempt: its retry is still to run. --log shows why it failed.
        /// </summary>
        public int FailedAttempts => Volatile.Read(ref _failedAttempts);

        /// <summary>
        /// Runs of XactJobs' own periodic jobs, e.g. the history cleanup, every hour on the hour: not the benchmark's jobs, not counted.
        /// </summary>
        public int PeriodicRuns => Volatile.Read(ref _periodicRuns);

        public Task FirstCompleted => _firstCompleted.Task;

        public Task AllCompleted => _allCompleted.Task;

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var allHistoryRows = eventData.Context!.ChangeTracker.Entries<XactJobHistory>().ToList();

            // the benchmark's jobs: a run of XactJobs' own periodic jobs (e.g. the hourly history cleanup, if it's turned on) would end
            // the drain one job early otherwise
            var historyRows = allHistoryRows.Where(e => e.Entity.PeriodicJobId == null).ToList();

            Interlocked.Add(ref _periodicRuns, allHistoryRows.Count - historyRows.Count);

            // only the completions end the drain: a failed attempt's retry is still to run
            var completedRows = historyRows.Count(e => e.Entity.Status == XactJobStatus.Completed);

            Interlocked.Add(ref _failedAttempts, historyRows.Count - completedRows);

            if (completedRows > 0)
            {
                var count = Interlocked.Add(ref _count, completedRows);

                _firstCompleted.TrySetResult();

                if (count >= expected) _allCompleted.TrySetResult();
            }

            return ValueTask.FromResult(result);
        }
    }

    /// <summary>
    /// Counts the SQL commands, with the time EF measured for each (executing it and reading its results), and with --sql lists them.
    /// </summary>
    private sealed class CommandCounter : DbCommandInterceptor
    {
        private readonly ConcurrentDictionary<string, (int Count, long Ticks)> _byCommand = new();

        private long _count;
        private long _ticks;

        public long Count => Interlocked.Read(ref _count);

        public TimeSpan Time => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

        public void Reset()
        {
            Interlocked.Exchange(ref _count, 0);
            Interlocked.Exchange(ref _ticks, 0);
            _byCommand.Clear();
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            Add(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            Add(command, eventData);
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Add(command, eventData);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
        {
            Add(command, eventData);
            return ValueTask.FromResult(result);
        }

        public void Print(string name, int jobs)
        {
            Console.WriteLine($"  SQL of {name}, per job:");

            foreach (var (text, (count, ticks)) in _byCommand.OrderByDescending(c => c.Value.Ticks))
            {
                Console.WriteLine($"    {(double)count / jobs,8:F3} x {TimeSpan.FromTicks(ticks).TotalMicroseconds / jobs,9:F1} µs  {text}");
            }
        }

        private void Add(DbCommand command, CommandExecutedEventData eventData)
        {
            Interlocked.Increment(ref _count);
            Interlocked.Add(ref _ticks, eventData.Duration.Ticks);

            var text = string.Join(' ', command.CommandText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            _byCommand.AddOrUpdate(text.Length > 110 ? text[..110] : text, (1, eventData.Duration.Ticks), (_, c) => (c.Count + 1, c.Ticks + eventData.Duration.Ticks));
        }
    }

    /// <summary>
    /// One drain, from its first completed job to its last: wall time, CPU time of the whole process (all threads, SQLite's work
    /// included), the bytes allocated on all threads, and the SQL commands with the time EF measured for them.
    /// </summary>
    private readonly record struct Measurement(int Jobs, TimeSpan Wall, double WallPerJob, double CpuPerJob, double KBPerJob, double CommandsPerJob, double SqlPerJob)
    {
        public static async Task<Measurement> MeasureAsync(CompletedJobCounter completed, CommandCounter commands, Task? startAfter = null)
        {
            await completed.FirstCompleted;

            if (startAfter != null) await startAfter;

            // measured from the moment all the workers poll (each one's first poll waits for its polling interval): with too few jobs,
            // most of the backlog is gone by then, and the rest says nothing
            var done = completed.Count;

            if (done * 2 > completed.Expected)
            {
                throw new InvalidOperationException(
                    $"{done} of the {completed.Expected} jobs were done before the measurement started, once all the workers poll: " +
                    "use more jobs (--jobs), so that most of the drain is measured");
            }

            var process = Process.GetCurrentProcess();

            commands.Reset();

            var completedBefore = completed.Count;
            var cpu = process.TotalProcessorTime;
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var stopwatch = Stopwatch.StartNew();

            await completed.AllCompleted;

            stopwatch.Stop();
            process.Refresh();

            var jobs = completed.Expected - completedBefore;

            return new Measurement(jobs,
                                   stopwatch.Elapsed,
                                   stopwatch.Elapsed.TotalMicroseconds / jobs,
                                   (process.TotalProcessorTime - cpu).TotalMicroseconds / jobs,
                                   (GC.GetTotalAllocatedBytes(precise: true) - allocated) / 1024.0 / jobs,
                                   (double)commands.Count / jobs,
                                   commands.Time.TotalMicroseconds / jobs);
        }

        public static Measurement Median(List<Measurement> runs) => new(
            runs[0].Jobs,
            TimeSpan.FromTicks((long)Median(runs.Select(r => (double)r.Wall.Ticks))),
            Median(runs.Select(r => r.WallPerJob)),
            Median(runs.Select(r => r.CpuPerJob)),
            Median(runs.Select(r => r.KBPerJob)),
            Median(runs.Select(r => r.CommandsPerJob)),
            Median(runs.Select(r => r.SqlPerJob)));

        private static double Median(IEnumerable<double> values)
        {
            var sorted = values.Order().ToArray();

            return sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2;
        }
    }

    /// <summary>
    /// A database with the schema, and the backlog of jobs, due now. A new one for each drain.
    /// </summary>
    private abstract class BenchDatabase : IAsyncDisposable
    {
        public static async Task<BenchDatabase> CreateAsync(DrainSettings settings, int jobs)
        {
            BenchDatabase database = settings.Postgres != null ? new PostgresDatabase(settings.Postgres)
                : settings.SqlServer != null ? new SqlServerDatabase(settings.SqlServer)
                : settings.MySql != null ? new MySqlDatabase(settings.MySql)
                : new InMemoryDatabase();

            await database.CreateSchemaAsync();

            await using (var db = database.CreateDb())
            {
                var dueAt = DateTime.UtcNow.AddMinutes(-1);

                for (var i = 0; i < jobs; i++)
                {
                    db.JobScheduleAt(() => BenchmarkJobs.Noop(), dueAt);
                }

                await db.SaveChangesAsync();
            }

            await database.AnalyzeAsync();

            return database;
        }

        public abstract DbContextOptionsBuilder Configure(DbContextOptionsBuilder options);

        protected abstract Task CreateSchemaAsync();

        protected virtual Task AnalyzeAsync() => Task.CompletedTask;

        public abstract ValueTask DisposeAsync();

        public async Task AssertDrainedAsync(int jobs)
        {
            await using var db = CreateDb();

            // XactJobs' own periodic jobs, e.g. the history cleanup if it's turned on, aren't the benchmark's
            var left = await db.Jobs.CountAsync(j => j.PeriodicJobId == null);
            var completed = await db.JobHistory.CountAsync(h => h.PeriodicJobId == null && h.Status == XactJobStatus.Completed);

            if (left != 0 || completed != jobs)
            {
                var leftRows = await db.Jobs.AsNoTracking().Where(j => j.PeriodicJobId == null).Take(5).ToListAsync();
                var otherHistory = await db.JobHistory.AsNoTracking().Where(h => h.PeriodicJobId == null && h.Status != XactJobStatus.Completed).Take(5).ToListAsync();

                foreach (var j in leftRows)
                {
                    Console.WriteLine($"  left: id={j.Id} scheduled_at={j.ScheduledAt:O} errors={j.ErrorCount} leased_until={j.LeasedUntil:O} originally={j.OriginallyScheduledAt:O}");
                }

                foreach (var h in otherHistory)
                {
                    Console.WriteLine($"  history: id={h.Id} status={h.Status} processed_at={h.ProcessedAt:O} error={h.ErrorMessage}");
                }

                throw new InvalidOperationException($"Expected {jobs} completed jobs and none left, but {completed} completed and {left} are left");
            }
        }

        protected BenchDbContext CreateDb() => new((DbContextOptions<BenchDbContext>)Configure(new DbContextOptionsBuilder<BenchDbContext>()).Options);

        protected static string GetScript(params string[] path)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "sql", "install")))
            {
                directory = directory.Parent;
            }

            if (directory == null) throw new DirectoryNotFoundException("The sql folder of the repository wasn't found");

            return Path.Combine([directory.FullName, "sql", .. path]);
        }
    }

    /// <summary>
    /// A shared in-memory SQLite database, with the schema from the install script.
    /// </summary>
    private sealed class InMemoryDatabase : BenchDatabase
    {
        private readonly string _connectionString = $"Data Source=bench-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;";

        // keeps the database alive
        private readonly SqliteConnection _connection;

        public InMemoryDatabase()
        {
            _connection = new SqliteConnection(_connectionString);
        }

        public override DbContextOptionsBuilder Configure(DbContextOptionsBuilder options) => options.UseSqlite(_connectionString);

        protected override async Task CreateSchemaAsync()
        {
            await _connection.OpenAsync();

            await using var command = _connection.CreateCommand();

            command.CommandText = await File.ReadAllTextAsync(GetScript("install", "xact-jobs-sqlite.sql"));

            await command.ExecuteNonQueryAsync();
        }

        public override ValueTask DisposeAsync() => _connection.DisposeAsync();
    }

    /// <summary>
    /// A new PostgreSQL database on the server of the connection string, with the schema from the install script, and fresh statistics.
    /// Dropped afterwards, with its pool cleared: each database has a pool of its own.
    /// </summary>
    private sealed class PostgresDatabase(string serverConnectionString) : BenchDatabase
    {
        private readonly string _name = $"bench_{Guid.NewGuid():N}";

        private string ConnectionString => new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = _name }.ConnectionString;

        public override DbContextOptionsBuilder Configure(DbContextOptionsBuilder options) => options.UseNpgsql(ConnectionString);

        protected override async Task CreateSchemaAsync()
        {
            await ExecuteAsync(serverConnectionString, $"CREATE DATABASE {_name}");
            await ExecuteAsync(ConnectionString, await File.ReadAllTextAsync(GetScript("install", "xact-jobs-postgresql.sql")));
        }

        protected override Task AnalyzeAsync() => ExecuteAsync(ConnectionString, "ANALYZE");

        public override async ValueTask DisposeAsync()
        {
            await using (var connection = new NpgsqlConnection(ConnectionString))
            {
                NpgsqlConnection.ClearPool(connection);
            }

            await ExecuteAsync(serverConnectionString, $"DROP DATABASE IF EXISTS {_name} WITH (FORCE)");
        }

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using var connection = new NpgsqlConnection(connectionString);

            await connection.OpenAsync();

            await using var command = new NpgsqlCommand(sql, connection);

            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// A new SQL Server database on the server of the connection string, with the schema from the install script, and fresh statistics.
    /// Dropped afterwards, with its pool cleared: each database has a pool of its own.
    /// </summary>
    private sealed class SqlServerDatabase(string serverConnectionString) : BenchDatabase
    {
        private readonly string _name = $"bench_{Guid.NewGuid():N}";

        private string ConnectionString => new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = _name }.ConnectionString;

        public override DbContextOptionsBuilder Configure(DbContextOptionsBuilder options) => options.UseSqlServer(ConnectionString);

        protected override async Task CreateSchemaAsync()
        {
            await ExecuteAsync(serverConnectionString, $"CREATE DATABASE [{_name}]");
            await ExecuteAsync(ConnectionString, await File.ReadAllTextAsync(GetScript("install", "xact-jobs-sqlserver.sql")));
        }

        protected override Task AnalyzeAsync() => ExecuteAsync(ConnectionString, "EXEC sp_updatestats");

        public override async ValueTask DisposeAsync()
        {
            await using (var connection = new SqlConnection(ConnectionString))
            {
                SqlConnection.ClearPool(connection);
            }

            await ExecuteAsync(serverConnectionString, $"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}]");
        }

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using var connection = new SqlConnection(connectionString);

            await connection.OpenAsync();

            await using var command = new SqlCommand(sql, connection);

            await command.ExecuteNonQueryAsync();
        }
    }

    /// <summary>
    /// A new MySQL database on the server of the connection string, with the schema from the install script, and fresh statistics.
    /// The official provider (MySql.EntityFrameworkCore), which supports EF Core 10. Dropped afterwards, with its pool cleared.
    /// </summary>
    private sealed class MySqlDatabase(string serverConnectionString) : BenchDatabase
    {
        private readonly string _name = $"bench_{Guid.NewGuid():N}";

        private string ConnectionString => new MySqlConnectionStringBuilder(serverConnectionString) { Database = _name }.ConnectionString;

        public override DbContextOptionsBuilder Configure(DbContextOptionsBuilder options) => options.UseMySQL(ConnectionString);

        protected override async Task CreateSchemaAsync()
        {
            await ExecuteAsync(serverConnectionString, $"CREATE DATABASE `{_name}`");
            await ExecuteAsync(ConnectionString, await File.ReadAllTextAsync(GetScript("install", "xact-jobs-mysql.sql")));
        }

        protected override Task AnalyzeAsync() => ExecuteAsync(ConnectionString, "ANALYZE TABLE `xact_jobs_job`, `xact_jobs_job_history`");

        public override async ValueTask DisposeAsync()
        {
            await using (var connection = new MySqlConnection(ConnectionString))
            {
                MySqlConnection.ClearPool(connection);
            }

            await ExecuteAsync(serverConnectionString, $"DROP DATABASE IF EXISTS `{_name}`");
        }

        private static async Task ExecuteAsync(string connectionString, string sql)
        {
            await using var connection = new MySqlConnection(connectionString);

            await connection.OpenAsync();

            await using var command = new MySqlCommand(sql, connection);

            await command.ExecuteNonQueryAsync();
        }
    }
}
