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
using XactJobs.Internal.SqlDialects;

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
        var parallelism = GetOption(args, "--parallelism", 1);
        var listSql = args.Contains("--sql");
        var only = GetOnly(args);

        if (jobs < 2) throw new ArgumentException("--jobs needs at least 2: the first one only starts the measurement");

        Console.WriteLine($"Drain: {jobs} jobs, {parallelism} at a time, median of {runs} runs after a warm-up");
        Console.WriteLine($".NET {Environment.Version}, EF Core {GetVersion(typeof(DbContext))}, XactJobs {GetVersion(typeof(XactJob))}, SQLite in memory, " +
                          $"{Environment.ProcessorCount} processors");
#if DEBUG
        Console.WriteLine("WARNING: a Debug build. Run with -c Release.");
#endif
        Console.WriteLine();

        var runXactJobs = only is null or "xactjobs";
        var runEf = only is null or "ef";

        // warm-up: JIT, EF's model and query caches
        if (runXactJobs) await DrainWithXactJobsAsync(Math.Min(jobs, 500), parallelism, listSql: false);
        if (runEf) await DrainWithEfAsync(Math.Min(jobs, 500), parallelism, listSql: false);

        List<Measurement> xactJobs = [];
        List<Measurement> ef = [];

        for (var run = 0; run < runs; run++)
        {
            // alternated, so that a slower moment of the machine doesn't hit only one of them
            if (runXactJobs) xactJobs.Add(await DrainWithXactJobsAsync(jobs, parallelism, listSql && run == 0));
            if (runEf) ef.Add(await DrainWithEfAsync(jobs, parallelism, listSql && run == 0));

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
        }

        return 0;
    }

    private static async Task<Measurement> DrainWithXactJobsAsync(int jobs, int parallelism, bool listSql)
    {
        await using var database = await InMemoryDatabase.CreateAsync(jobs);

        var completed = new CompletedJobCounter(jobs);
        var commands = new CommandCounter();

        var builder = Host.CreateApplicationBuilder();

        builder.Logging.ClearProviders();

        builder.Services.AddDbContext<BenchDbContext>(options => options.UseSqlite(database.ConnectionString).AddInterceptors(completed, commands));

        builder.Services.AddXactJobs<BenchDbContext>(options => options
            .WithWorkerCount(1)
            .WithMaxDegreeOfParallelism(parallelism)
            .WithPollingInterval(1)
            .WithShutdownTimeout(0));

        using var host = builder.Build();

        await host.StartAsync();

        var measurement = await Measurement.MeasureAsync(completed, commands);

        await host.StopAsync();

        await database.AssertDrainedAsync(jobs);

        if (listSql) commands.Print("XactJobs", measurement.Jobs);

        return measurement;
    }

    private static async Task<Measurement> DrainWithEfAsync(int jobs, int parallelism, bool listSql)
    {
        await using var database = await InMemoryDatabase.CreateAsync(jobs);

        var completed = new CompletedJobCounter(jobs);
        var commands = new CommandCounter();

        await using var services = new ServiceCollection()
            .AddDbContext<BenchDbContext>(options => options.UseSqlite(database.ConnectionString).AddInterceptors(completed, commands))
            .BuildServiceProvider();

        var leaser = Guid.NewGuid();

        var slots = Task.WhenAll(Enumerable.Range(0, parallelism).Select(_ => Task.Run(() => RunSlotAsync(services, leaser))));

        var measurement = await Measurement.MeasureAsync(completed, commands);

        await slots;

        await database.AssertDrainedAsync(jobs);

        if (listSql) commands.Print("plain EF", measurement.Jobs);

        return measurement;
    }

    /// <summary>
    /// Claims a job, completes it, and claims the next one, until there's none left.
    /// </summary>
    private static async Task RunSlotAsync(IServiceProvider services, Guid leaser)
    {
        var dialect = new SqliteDialect();

        while (true)
        {
            List<XactJob> claimed;

            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BenchDbContext>();

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

        public int Expected => expected;

        public int Count => Volatile.Read(ref _count);

        public Task FirstCompleted => _firstCompleted.Task;

        public Task AllCompleted => _allCompleted.Task;

        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var historyRows = eventData.Context!.ChangeTracker.Entries<XactJobHistory>().Count();

            if (historyRows > 0)
            {
                var count = Interlocked.Add(ref _count, historyRows);

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
        public static async Task<Measurement> MeasureAsync(CompletedJobCounter completed, CommandCounter commands)
        {
            await completed.FirstCompleted;

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
    /// A shared in-memory database with the schema from the SQL scripts, and the backlog of jobs, due now.
    /// </summary>
    private sealed class InMemoryDatabase : IAsyncDisposable
    {
        // keeps the database alive
        private readonly SqliteConnection _connection;

        private InMemoryDatabase(string connectionString)
        {
            ConnectionString = connectionString;
            _connection = new SqliteConnection(connectionString);
        }

        public string ConnectionString { get; }

        public static async Task<InMemoryDatabase> CreateAsync(int jobs)
        {
            var database = new InMemoryDatabase($"Data Source=bench-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;");

            await database._connection.OpenAsync();

            foreach (var script in GetSqliteScripts())
            {
                await using var command = database._connection.CreateCommand();

                command.CommandText = await File.ReadAllTextAsync(script);

                await command.ExecuteNonQueryAsync();
            }

            await using var db = database.CreateDb();

            var dueAt = DateTime.UtcNow.AddMinutes(-1);

            for (var i = 0; i < jobs; i++)
            {
                db.JobScheduleAt(() => BenchmarkJobs.Noop(), dueAt);
            }

            await db.SaveChangesAsync();

            return database;
        }

        public async Task AssertDrainedAsync(int jobs)
        {
            await using var db = CreateDb();

            // the host adds XactJobs' own periodic job (the history cleanup), and its next run
            var left = await db.Jobs.CountAsync(j => j.PeriodicJobId == null);
            var completed = await db.JobHistory.CountAsync(h => h.PeriodicJobId == null && h.Status == XactJobStatus.Completed);

            if (left != 0 || completed != jobs)
            {
                throw new InvalidOperationException($"Expected {jobs} completed jobs and none left, but {completed} completed and {left} are left");
            }
        }

        public ValueTask DisposeAsync() => _connection.DisposeAsync();

        private BenchDbContext CreateDb() => new(new DbContextOptionsBuilder<BenchDbContext>().UseSqlite(ConnectionString).Options);

        private static IEnumerable<string> GetSqliteScripts()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);

            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "sql", "v0.1.0")))
            {
                directory = directory.Parent;
            }

            if (directory == null) throw new DirectoryNotFoundException("The sql folder of the repository wasn't found");

            return new DirectoryInfo(Path.Combine(directory.FullName, "sql"))
                .GetDirectories("v*")
                .OrderBy(d => Version.Parse(d.Name[1..]))
                .Select(d => Path.Combine(d.FullName, $"xact-jobs-sqlite-{d.Name}.sql"))
                .Where(File.Exists);
        }
    }
}
