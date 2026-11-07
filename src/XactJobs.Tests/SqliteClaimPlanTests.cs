using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using XactJobs.Internal.SqlDialects;

namespace XactJobs.Tests;

/// <summary>
/// On SQLite the primary key is only the id, so the claims need the index on (queue, scheduled_at): without it, each claim reads
/// and sorts the whole job table, and a backlog gets slower to drain the bigger it is. Checked with SQLite's query plan, on the
/// schema from the SQL scripts and on the one EF creates from the model.
/// </summary>
public sealed partial class SqliteClaimPlanTests : IDisposable
{
    private readonly SqliteDialect _dialect = new();
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public SqliteClaimPlanTests()
    {
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    [Theory]
    [InlineData("scripts")]
    [InlineData("model")]
    public void JobClaim_SearchesTheIndex_AndDoesntSort(string schema)
    {
        CreateSchema(schema);

        var plan = Explain(_dialect.GetFetchJobsSql(QueueNames.Default, 10, Guid.NewGuid(), 60));

        Assert.True(plan.Any(step => step.StartsWith("SEARCH xact_jobs_job USING INDEX ix_job_queue_scheduled_at (queue=? AND scheduled_at<?)")), Describe(plan));
        Assert.False(plan.Any(IsFullScanOfTheJobTable), Describe(plan));
        Assert.False(plan.Any(step => step.Contains("TEMP B-TREE")), Describe(plan));
    }

    [Theory]
    [InlineData("scripts")]
    [InlineData("model")]
    public void ContinuationClaim_SearchesAnIndex_AndDoesntReadTheWholeTable(string schema)
    {
        CreateSchema(schema);

        var plan = Explain(_dialect.GetFetchContinuationsSql(QueueNames.Default, 10, Guid.NewGuid(), 60, DateTime.UtcNow));

        Assert.True(plan.Any(step => step.StartsWith("SEARCH c USING INDEX")), Describe(plan));
        Assert.False(plan.Any(IsFullScanOfTheJobTable), Describe(plan));
    }

    private static bool IsFullScanOfTheJobTable(string step) => FullScanOfTheJobTable().IsMatch(step);

    // "SCAN xact_jobs_job" or "SCAN c" (the alias), but not "SCAN cte", nor a scan of an index ("SCAN c USING INDEX ...")
    [GeneratedRegex(@"^SCAN (xact_jobs_job|c|b)$")]
    private static partial Regex FullScanOfTheJobTable();

    private void CreateSchema(string schema)
    {
        if (schema == "scripts")
        {
            foreach (var script in GetSqliteMigrationScripts())
            {
                using var command = _connection.CreateCommand();

                command.CommandText = File.ReadAllText(script);
                command.ExecuteNonQuery();
            }
        }
        else
        {
            using var db = new TestDbContext(new DbContextOptionsBuilder<TestDbContext>().UseSqlite(_connection).Options);

            db.Database.EnsureCreated();
        }
    }

    /// <summary>
    /// The steps of SQLite's query plan for a dialect's statement, whose {0}, {1}... placeholders become parameters.
    /// </summary>
    private List<string> Explain((string Sql, object[] Parameters) statement)
    {
        using var command = _connection.CreateCommand();

        command.CommandText = "EXPLAIN QUERY PLAN " + Placeholder().Replace(statement.Sql, match => "$p" + match.Groups[1].Value);

        for (var i = 0; i < statement.Parameters.Length; i++)
        {
            command.Parameters.AddWithValue("$p" + i, statement.Parameters[i]);
        }

        using var reader = command.ExecuteReader();

        List<string> steps = [];

        while (reader.Read())
        {
            steps.Add(reader.GetString(reader.GetOrdinal("detail")));
        }

        return steps;
    }

    [GeneratedRegex(@"\{(\d+)\}")]
    private static partial Regex Placeholder();

    private static string Describe(List<string> plan) => "Plan:\n" + string.Join("\n", plan);

    private static IEnumerable<string> GetSqliteMigrationScripts()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "sql", "v0.1.0")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return new DirectoryInfo(Path.Combine(directory.FullName, "sql"))
            .GetDirectories("v*")
            .OrderBy(d => Version.Parse(d.Name[1..]))
            .Select(d => Path.Combine(d.FullName, $"xact-jobs-sqlite-{d.Name}.sql"))
            .Where(File.Exists);
    }
}
