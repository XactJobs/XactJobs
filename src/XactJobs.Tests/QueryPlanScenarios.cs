using System.Data.Common;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using XactJobs.Internal;
using XactJobs.Internal.SqlDialects;
using XactJobs.UI.Services;
using XactJobs.UI.Services.Models;

namespace XactJobs.Tests;

/// <summary>
/// The plans of the queries that look for job rows outside the claims: they must use an index, not read the whole job table,
/// which can be large (a backlog). Each query EF sends is explained by the database first, with its parameters:
/// <list type="bullet">
/// <item>the dashboard's lookups of a pending job use the primary key, which is (queue, scheduled time, id) except on SQLite;</item>
/// <item>the queries of the runs of a periodic job use the index of the periodic job column, which holds only the runs of periodic
/// jobs. MySQL has no such index: it has no filtered indexes, and a full one would be written by every job.</item>
/// <item>the queries of a batch's jobs and continuations use the indexes of the batch columns, which hold only the rows of batches
/// and continuations (except on MySQL, which has no filtered indexes), also the continuation claim's check that a batch has no jobs
/// left, which compares with the continuation's column, not with a parameter.</item>
/// </list>
/// The table gets enough rows, and statistics, for the planners to prefer an index. The scenarios run on every database: see
/// <c>Databases/QueryPlanIntegrationTests</c> and <see cref="SqliteQueryPlanTests"/>.
/// </summary>
public sealed class QueryPlanScenarios(Action<DbContextOptionsBuilder> configure)
{
    private const string NotPolled = "not-polled";
    // a backlog: with a few thousand rows, the job table is a few dozen pages, and SQL Server rightly prefers reading them all to
    // looking up the 20 runs of the periodic jobs one by one through the index
    private const int OneOffJobs = 10_000;
    private const int PeriodicJobs = 20;

    public async Task DashboardJobLookups_UseThePrimaryKey()
    {
        var job = await AddRowsAsync();

        var recorder = new PlanRecorder(IsJobTableQuery);

        await using (var db = CreateDb(recorder))
        {
            var service = new XactJobsUIService<TestDbContext>(db);

            Assert.NotNull(await service.GetJobAsync(job));

            // cancels it: finds it by its key first
            Assert.Equal(ActionStatus.Done, (await service.CancelJobAsync(job, null)).Status);
        }

        var plans = await GetPlansAsync(recorder);

        Assert.NotEmpty(plans);

        // the reads, and the delete of the cancelled row
        Assert.Contains(plans, p => p.Sql.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase));

        foreach (var (sql, plan) in plans)
        {
            AssertUsesTheIndex(PrimaryKey, sql, plan);
        }
    }

    public async Task PeriodicJobQueries_UseThePeriodicJobIndex()
    {
        await AddRowsAsync();

        var recorder = new PlanRecorder(sql => IsJobTableQuery(sql) && sql.Contains(Column(nameof(XactJob.PeriodicJobId)), StringComparison.OrdinalIgnoreCase));

        // paused, so that it can be resumed below
        await using (var db = CreateDb(null))
        {
            await db.JobSetPeriodicActiveAsync("periodic-3", false, CancellationToken.None);
            await db.SaveChangesAsync();
        }

        await using (var db = CreateDb(recorder))
        {
            // paused: looks for its pending runs (not saved)
            await db.JobSetPeriodicActiveAsync("periodic-1", false, CancellationToken.None);

            // resumed: looks for a pending run first (not saved)
            await db.JobSetPeriodicActiveAsync("periodic-3", true, CancellationToken.None);

            // the reconciliation's query (XactJobsCronScheduler.ReconcilePeriodicJobsAsync): the periodic jobs without a run of their version
            await db.Set<XactJobPeriodic>()
                .AsNoTracking()
                .Where(p => !db.Set<XactJob>().Any(j => j.PeriodicJobId == p.Id && (j.PeriodicJobVersion ?? 1) == p.Version))
                .ToListAsync();
        }

        await using (var db = CreateDb(recorder))
        {
            var service = new XactJobsUIService<TestDbContext>(db);

            // the Periodic page: the runs of the listed periodic jobs
            Assert.Equal(PeriodicJobs, (await service.GetPeriodicJobsAsync(1, 100)).Items.Count);

            // Trigger: looks for the pending run, and runs it now
            Assert.Equal(ActionStatus.Done, (await service.TriggerPeriodicJobAsync("periodic-2")).Status);
        }

        var plans = await GetPlansAsync(recorder);

        Assert.True(plans.Count >= 5, $"Expected the queries of the periodic jobs' runs, but got:\n{string.Join("\n", plans.Select(p => p.Sql))}");

        foreach (var (sql, plan) in plans)
        {
            AssertUsesTheIndex(PeriodicJobIndex, sql, plan);
        }
    }

    public async Task BatchQueries_UseTheBatchIndexes()
    {
        await AddRowsAsync();

        // a batch with jobs left, and a finished one (no jobs left), each with a continuation waiting for it
        var active = Guid.NewGuid();
        var finished = Guid.NewGuid();

        await using (var db = CreateDb(null))
        {
            var dueAt = DateTime.UtcNow.AddHours(1);

            for (var i = 0; i < 3; i++)
            {
                db.Jobs.Add(new XactJob(0, dueAt.AddSeconds(i), "T", "M()", "[]", NotPolled) { BatchId = active });
            }

            db.Jobs.Add(new XactJob(0, XactJobBase.AwaitingBatchScheduledAt, "T", "M()", "[]", NotPolled) { AwaitsBatchId = active });
            db.Jobs.Add(new XactJob(0, XactJobBase.AwaitingBatchScheduledAt, "T", "M()", "[]", NotPolled) { AwaitsBatchId = finished });

            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlRawAsync(Statistics);
        }

        var recorder = new PlanRecorder(sql => IsJobTableQuery(sql)
            && (sql.Contains(Column(nameof(XactJob.BatchId)), StringComparison.OrdinalIgnoreCase)
                || sql.Contains(Column(nameof(XactJob.AwaitsBatchId)), StringComparison.OrdinalIgnoreCase)));

        await using (var db = CreateDb(recorder))
        {
            // Oracle claims in a PL/SQL block, which can't be explained (and its index leaves out the null rows on its own)
            if (Provider != Db.Oracle)
            {
                // the claim of the continuations whose batch is finished: checks for each one that its batch has no jobs left
                List<XactJob> claimed = await Dialect.ClaimContinuationsAsync(db, NotPolled, 10, Guid.NewGuid(), 60, DateTime.UtcNow, CancellationToken.None);

                Assert.Equal(finished, Assert.Single(claimed).AwaitsBatchId);
            }

            // the worker that finished a batch's last job (XactJobRunner.StartContinuationsIfBatchFinishedAsync): the queues of its
            // continuations, if no job of the batch is left. With a parameter for each column, as there (Oracle)
            var jobs = db.Set<XactJob>();
            var awaitedBatchId = active;

            Assert.Empty(await jobs
                .Where(c => c.AwaitsBatchId == awaitedBatchId
                    && c.ScheduledAt == XactJobBase.AwaitingBatchScheduledAt
                    && !jobs.Any(j => j.BatchId == active))
                .Select(c => c.Queue)
                .Distinct()
                .ToListAsync());
        }

        await using (var db = CreateDb(recorder))
        {
            // the Batch page: the batch's jobs and continuations, and its summary
            Assert.NotNull(await new XactJobsUIService<TestDbContext>(db).GetBatchAsync(active));
        }

        var plans = await GetPlansAsync(recorder);

        Assert.True(plans.Count >= 4, $"Expected the queries of the batches, but got:\n{string.Join("\n", plans.Select(p => p.Sql))}");

        // or the primary key: the batch's summary reads its sample jobs by their id
        foreach (var (sql, plan) in plans)
        {
            AssertUsesTheIndex($"(?:{BatchIndex})|(?:{PrimaryKey})", sql, plan);
        }
    }

    // ---- per database

    private string ProviderName { get; set; } = "";

    private string JobTable => Provider switch
    {
        Db.PostgreSql => @"xact_jobs\.job\b",
        Db.SqlServer => @"\[XactJobs\]\.\[Job\]",
        Db.Oracle => "\"XACT_JOBS_JOB\"",
        Db.MySql => "`xact_jobs_job`",
        _ => "\"xact_jobs_job\"",
    };

    private string Column(string property)
    {
        var snakeCase = Regex.Replace(property, "(?<!^)([A-Z])", "_$1");

        return Provider switch
        {
            Db.SqlServer => property,
            Db.Oracle => snakeCase.ToUpperInvariant(),
            _ => snakeCase.ToLowerInvariant(),
        };
    }

    // patterns of the plans: MySQL may use its unique key on the id instead (InnoDB needs one for the auto increment), as direct
    private string PrimaryKey => Provider switch
    {
        Db.PostgreSql => @"(using|Bitmap Index Scan on) pk_job\b",
        Db.SqlServer => @"Index=""\[PK_Job\]""",
        Db.Oracle => @"\|\s*PK_JOB\s",
        Db.MySql => @"^table=\w+ type=(const|eq_ref|ref|range) key=(PRIMARY|id)$",
        _ => @"USING INTEGER PRIMARY KEY",
    };

    private string PeriodicJobIndex => Provider switch
    {
        Db.PostgreSql => @"(using|Bitmap Index Scan on) ix_job_periodic_job_id_version\b",
        Db.SqlServer => @"Index=""\[IX_Job_PeriodicJobId_Version\]""",
        Db.Oracle => @"\|\s*IX_JOB_PERIODIC_JOB_ID_VERSION\s",
        Db.MySql => @"^table=\w+ type=(const|eq_ref|ref|range) key=ix_job_periodic_job_id_version$",
        _ => @"USING (COVERING )?INDEX ix_job_periodic_job_id_version\b",
    };

    // either of the batch columns' indexes: of the batch's jobs, or of its continuations
    private string BatchIndex => Provider switch
    {
        Db.PostgreSql => @"(using|Bitmap Index Scan on) ix_job_(awaits_)?batch_id\b",
        Db.SqlServer => @"Index=""\[IX_Job_(Awaits)?BatchId\]""",
        Db.Oracle => @"\|\s*IX_JOB_(AWAITS_)?BATCH_ID\s",
        Db.MySql => @"^table=\w+ type=(const|eq_ref|ref|range) key=ix_job_(awaits_)?batch_id$",
        _ => @"USING (COVERING )?INDEX ix_job_(awaits_)?batch_id\b",
    };

    private enum Db { PostgreSql, SqlServer, Oracle, MySql, Sqlite }

    private Db Provider => ProviderName switch
    {
        var p when p.EndsWith(".PostgreSQL") => Db.PostgreSql,
        var p when p.EndsWith(".SqlServer") => Db.SqlServer,
        var p when p.StartsWith("Oracle.") => Db.Oracle,
        var p when p.Contains("MySql", StringComparison.OrdinalIgnoreCase) => Db.MySql,
        var p when p.EndsWith(".Sqlite") => Db.Sqlite,
        _ => throw new NotSupportedException(ProviderName),
    };

    private bool IsJobTableQuery(string sql) => Regex.IsMatch(sql, JobTable);

    /// <summary>
    /// The plan uses the index, and doesn't read the whole job table, nor the whole of another of its indexes. Reading the whole
    /// index of the periodic job column is fine: it holds only the runs of periodic jobs. So do the batch columns' indexes, which hold
    /// only the rows of batches and continuations, except on MySQL (no filtered indexes): reading all of one of MySQL's reads every job.
    /// </summary>
    private void AssertUsesTheIndex(string index, string sql, string plan)
    {
        var message = $"SQL:\n{sql}\n\nPlan:\n{plan}";

        Assert.True(Regex.IsMatch(plan, index, RegexOptions.Multiline), $"The plan doesn't match {index}.\n{message}");
        Assert.False(ReadsTheWholeJobTable(sql, plan), $"The plan reads the whole job table.\n{message}");
    }

    private bool ReadsTheWholeJobTable(string sql, string plan)
    {
        switch (Provider)
        {
            case Db.PostgreSql:
                return Regex.IsMatch(plan, @"Seq Scan on job\b");

            case Db.SqlServer:
                XNamespace ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

                return XDocument.Parse(plan).Descendants(ns + "RelOp")
                    .Where(op => (string?)op.Attribute("PhysicalOp") is "Clustered Index Scan" or "Table Scan" or "Index Scan")
                    .SelectMany(op => op.Elements().Where(e => e.Name.LocalName is "IndexScan" or "TableScan").Elements(ns + "Object"))
                    .Any(o => (string?)o.Attribute("Table") == "[Job]" && (string?)o.Attribute("Index") is not ("[IX_Job_PeriodicJobId_Version]" or "[IX_Job_BatchId]" or "[IX_Job_AwaitsBatchId]"));

            case Db.Oracle:
                return Regex.IsMatch(plan, @"TABLE ACCESS (STORAGE )?FULL\s*\|\s*XACT_JOBS_JOB\s")
                    || Regex.IsMatch(plan, @"INDEX (FAST )?FULL SCAN\s*\|\s*(?!(IX_JOB_PERIODIC_JOB_ID_VERSION|IX_JOB_BATCH_ID|IX_JOB_AWAITS_BATCH_ID)\s)\w+\s");

            case Db.MySql:
                return JobTableAliases(sql, "`xact_jobs_job`", "`")
                    .Any(alias => Regex.IsMatch(plan, $@"^table={Regex.Escape(alias)} type=(ALL|index) ", RegexOptions.Multiline));

            default:
                return JobTableAliases(sql, "\"xact_jobs_job\"", "\"")
                    .Any(alias => Regex.IsMatch(plan, $@"^SCAN {Regex.Escape(alias)}( USING (COVERING )?INDEX (?!(ix_job_periodic_job_id_version|ix_job_batch_id|ix_job_awaits_batch_id)\b)\w+.*)?$", RegexOptions.Multiline));
        }
    }

    /// <summary>
    /// The names the job table has in the query (MySQL's and SQLite's plans name tables by their alias).
    /// </summary>
    private static List<string> JobTableAliases(string sql, string table, string quote)
    {
        var q = Regex.Escape(quote);

        return [table.Trim(quote[0]), .. Regex.Matches(sql, $@"{Regex.Escape(table)} AS {q}(\w+){q}").Select(m => m.Groups[1].Value)];
    }

    // ---- data

    /// <summary>
    /// One-off jobs, and periodic jobs with their pending runs, in a queue no worker polls. Returns the key of a one-off job.
    /// </summary>
    private async Task<JobKey> AddRowsAsync()
    {
        await using (var db = CreateDb(null))
        {
            ProviderName = db.Database.ProviderName!;

            var dueAt = DateTime.UtcNow.AddHours(1);

            for (var i = 0; i < OneOffJobs; i++)
            {
                db.Jobs.Add(new XactJob(0, dueAt.AddSeconds(i), "T", "M()", "[]", NotPolled));
            }

            await db.SaveChangesAsync();

            for (var i = 0; i < PeriodicJobs; i++)
            {
                await db.JobEnsurePeriodicAsync<PlanJobs>(x => x.Run(), $"periodic-{i}", NotPolled, "0 0 0 * * *", CancellationToken.None);
            }

            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlRawAsync(Statistics);
        }

        await using (var db = CreateDb(null))
        {
            var job = await db.Jobs.AsNoTracking().Where(j => j.PeriodicJobId == null).OrderBy(j => j.Id).Skip(OneOffJobs / 2).FirstAsync();

            return JobKey.Of(job);
        }
    }

    // dynamic, like the fixtures' dialects: ISqlDialect is internal
    private dynamic Dialect => Provider switch
    {
        Db.PostgreSql => (object)new PostgreSqlDialect(),
        Db.SqlServer => new SqlServerDialect(),
        Db.Oracle => new OracleDialect(),
        Db.MySql => new MySqlDialect(),
        _ => (object)new SqliteDialect(),
    };

    private string Statistics => Provider switch
    {
        Db.PostgreSql => "ANALYZE xact_jobs.job",
        Db.SqlServer => "UPDATE STATISTICS [XactJobs].[Job]",
        Db.Oracle => "BEGIN DBMS_STATS.GATHER_TABLE_STATS(USER, 'XACT_JOBS_JOB'); END;",
        Db.MySql => "ANALYZE TABLE xact_jobs_job",
        _ => "ANALYZE",
    };

    private TestDbContext CreateDb(PlanRecorder? recorder)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>();

        configure(options);

        if (recorder != null)
        {
            recorder.Explain = Explain;
            options.AddInterceptors(recorder);
        }

        return new TestDbContext(options.Options);
    }

    // ---- explaining

    /// <summary>
    /// The database's plan of the command, with its parameters. The command isn't changed.
    /// </summary>
    private string Explain(DbCommand command)
    {
        var sql = command.CommandText;

        try
        {
            switch (Provider)
            {
                case Db.PostgreSql:
                    command.CommandText = "EXPLAIN " + sql;
                    return ReadAll(command, reader => reader.GetString(0));

                case Db.SqlServer:
                    // read from the plan cache once it ran (see GetPlansAsync): SET SHOWPLAN_XML doesn't apply to SqlClient's parameterized
                    // commands, which are sent as calls of sp_executesql, not as batches
                    return "";

                case Db.Oracle:
                    command.CommandText = "EXPLAIN PLAN FOR " + sql;
                    command.ExecuteNonQuery();

                    command.CommandText = "SELECT PLAN_TABLE_OUTPUT FROM TABLE(DBMS_XPLAN.DISPLAY())";

                    var parameters = command.Parameters.Cast<DbParameter>().ToList();
                    command.Parameters.Clear();

                    try
                    {
                        return ReadAll(command, reader => reader.GetString(0));
                    }
                    finally
                    {
                        foreach (var parameter in parameters) command.Parameters.Add(parameter);
                    }

                case Db.MySql:
                    command.CommandText = "EXPLAIN " + sql;
                    return ReadAll(command, reader => $"table={reader["table"]} type={reader["type"]} key={reader["key"]}".TrimEnd());

                default:
                    command.CommandText = "EXPLAIN QUERY PLAN " + sql;
                    return ReadAll(command, reader => reader.GetString(reader.GetOrdinal("detail")));
            }
        }
        finally
        {
            command.CommandText = sql;
        }
    }

    /// <summary>
    /// The recorded plans. On SQL Server, each command's plan as SQL Server compiled and cached it, for its parameters.
    /// </summary>
    private async Task<List<(string Sql, string Plan)>> GetPlansAsync(PlanRecorder recorder)
    {
        var plans = recorder.Plans;

        if (Provider != Db.SqlServer) return plans;

        await using var db = CreateDb(null);

        await db.Database.OpenConnectionAsync();

        await using var command = db.Database.GetDbConnection().CreateCommand();

        // the text of a parameterized command is its parameters' declarations, then the command's text
        command.CommandText = @"
SELECT TOP 1 CAST(qp.query_plan AS nvarchar(max))
FROM sys.dm_exec_query_stats AS qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) AS qp
WHERE CHARINDEX(@sql, st.text) > 0
ORDER BY qs.last_execution_time DESC";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@sql";
        command.Parameters.Add(parameter);

        List<(string Sql, string Plan)> cached = [];

        foreach (var (sql, _) in plans)
        {
            parameter.Value = sql;

            cached.Add((sql, (string?)await command.ExecuteScalarAsync() ?? throw new InvalidOperationException($"No cached plan of:\n{sql}")));
        }

        return cached;
    }

    private static string ReadAll(DbCommand command, Func<DbDataReader, string> read)
    {
        var plan = new StringBuilder();

        using var reader = command.ExecuteReader();

        do
        {
            while (reader.Read())
            {
                // \n, not AppendLine's Environment.NewLine: with \r\n (Windows), a pattern's $ doesn't match at the end of a line, since
                // in multiline mode .NET's $ matches only before \n
                plan.Append(read(reader)).Append('\n');
            }
        }
        while (reader.NextResult());

        return plan.ToString();
    }

    /// <summary>
    /// Explains the queries that match, before EF runs them.
    /// </summary>
    private sealed class PlanRecorder(Func<string, bool> shouldExplain) : DbCommandInterceptor
    {
        private readonly List<(string Sql, string Plan)> _plans = [];

        public Func<DbCommand, string> Explain { get; set; } = _ => "";

        public List<(string Sql, string Plan)> Plans
        {
            get { lock (_plans) return [.. _plans]; }
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            Record(command);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Record(command);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command)
        {
            // the statements that look for rows: not inserts, nor SaveChanges' batches (e.g. Oracle's PL/SQL blocks), which use the model's key
            if (!Regex.IsMatch(command.CommandText, @"^\s*(SELECT|WITH|UPDATE|DELETE)\b", RegexOptions.IgnoreCase)) return;

            if (!shouldExplain(command.CommandText)) return;

            var plan = Explain(command);

            lock (_plans) _plans.Add((command.CommandText, plan));
        }
    }

    public sealed class PlanJobs
    {
        public void Run()
        {
        }
    }
}
