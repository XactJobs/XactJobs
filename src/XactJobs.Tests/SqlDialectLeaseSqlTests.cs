using System.Data;
using System.Text.RegularExpressions;
using XactJobs.Internal.SqlDialects;

namespace XactJobs.Tests;

public class SqlDialectLeaseSqlTests
{
    private const int LeaseDuration = 45;

    private static readonly Guid Leaser = Guid.NewGuid();
    private static readonly DateTime ScheduledAt = new(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);
    private static readonly DateTime ClaimedAt = new(2026, 1, 2, 3, 4, 6, 123, DateTimeKind.Utc);

    private static readonly XactJob[] Jobs =
    [
        new XactJob(11, ScheduledAt, "Type", "Method", "[]", "queue-a"),
        new XactJob(22, ScheduledAt, "Type", "Method", "[]", "queue-b"),
    ];

    private static readonly Dictionary<string, (Func<(string Sql, object[] Parameters)> Extend, Func<(string Sql, object[] Parameters)> Clear)> Dialects = new()
    {
        ["postgresql"] = (() => new PostgreSqlDialect().GetExtendLeaseSql(Leaser, Jobs, LeaseDuration), () => new PostgreSqlDialect().GetClearLeaseSql(Leaser, Jobs)),
        ["sqlserver"] = (() => new SqlServerDialect().GetExtendLeaseSql(Leaser, Jobs, LeaseDuration), () => new SqlServerDialect().GetClearLeaseSql(Leaser, Jobs)),
        ["mysql"] = (() => new MySqlDialect().GetExtendLeaseSql(Leaser, Jobs, LeaseDuration), () => new MySqlDialect().GetClearLeaseSql(Leaser, Jobs)),
        ["oracle"] = (() => new OracleDialect().GetExtendLeaseSql(Leaser, Jobs, LeaseDuration), () => new OracleDialect().GetClearLeaseSql(Leaser, Jobs)),
        ["sqlite"] = (() => new SqliteDialect().GetExtendLeaseSql(Leaser, Jobs, LeaseDuration), () => new SqliteDialect().GetClearLeaseSql(Leaser, Jobs)),
    };

    public static TheoryData<string> DialectNames => new(Dialects.Keys);

    // only MySQL claims at another level than the default: see MySqlDialect
    [Fact]
    public void ClaimIsolationLevel_IsReadCommittedOnMySql_AndTheDefaultElsewhere()
    {
        Assert.Equal(IsolationLevel.ReadCommitted, new MySqlDialect().ClaimIsolationLevel);
        Assert.Null(new OracleDialect().ClaimIsolationLevel);
        Assert.Null(new PostgreSqlDialect().ClaimIsolationLevel);
        Assert.Null(new SqlServerDialect().ClaimIsolationLevel);
        Assert.Null(new SqliteDialect().ClaimIsolationLevel);
    }

    [Theory]
    [MemberData(nameof(DialectNames))]
    public void ExtendLeaseSql_HasNoInterpolatedValues_AndUsesEveryParameterOnce(string dialect)
    {
        var (sql, parameters) = Dialects[dialect].Extend();

        AssertFullyParameterized(sql, parameters);
        Assert.DoesNotContain(LeaseDuration.ToString(), sql);
    }

    [Theory]
    [MemberData(nameof(DialectNames))]
    public void ClearLeaseSql_HasNoInterpolatedValues_AndUsesEveryParameterOnce(string dialect)
    {
        var (sql, parameters) = Dialects[dialect].Clear();

        AssertFullyParameterized(sql, parameters);
    }

    [Theory]
    [InlineData("sqlserver", "[Queue] = {2} AND [ScheduledAt] = {3} AND [Id] = {4}")]
    [InlineData("mysql", "queue = {2} AND scheduled_at = {3} AND id = {4}")]
    [InlineData("oracle", "QUEUE = {2} AND SCHEDULED_AT = {3} AND ID = {4}")]
    public void ExtendLeaseSql_TargetsJobsByFullPrimaryKey(string dialect, string expectedFirstJobKey)
    {
        var (sql, parameters) = Dialects[dialect].Extend();

        Assert.Contains(expectedFirstJobKey, sql);
        Assert.Equal(["queue-a", ScheduledAt, 11L, "queue-b", ScheduledAt, 22L], parameters.Skip(2));
    }

    [Fact]
    public void PostgreSql_ExtendLeaseSql_PassesPrimaryKeysAsArrays()
    {
        var (sql, parameters) = Dialects["postgresql"].Extend();

        Assert.Contains("unnest({1}::text[], {2}::timestamptz[], {3}::bigint[])", sql);
        Assert.Equal(["queue-a", "queue-b"], (string[])parameters[1]);
        Assert.Equal([ScheduledAt, ScheduledAt], (DateTime[])parameters[2]);
        Assert.Equal([11L, 22L], (long[])parameters[3]);
        Assert.Equal(Leaser, parameters[4]);
    }

    [Fact]
    public void PostgreSql_FetchJobsSql_IsFullyParameterized()
    {
        var (sql, parameters) = new PostgreSqlDialect().GetFetchJobsSql("queue-a", 10, Leaser, LeaseDuration);

        AssertFullyParameterized(sql, parameters);
        Assert.Equal(["queue-a", 10, Leaser, LeaseDuration], parameters);
    }

    [Fact]
    public void SqlServer_FetchJobsSql_IsFullyParameterized()
    {
        var (sql, parameters) = new SqlServerDialect().GetFetchJobsSql("queue-a", 10, Leaser, LeaseDuration);

        AssertFullyParameterized(sql, parameters);
        Assert.Equal([10, "queue-a", Leaser, LeaseDuration], parameters);
    }

    [Fact]
    public void Sqlite_FetchJobsSql_IsFullyParameterized()
    {
        var (sql, parameters) = new SqliteDialect().GetFetchJobsSql("queue-a", 10, Leaser, LeaseDuration);

        AssertFullyParameterized(sql, parameters);
        Assert.Equal(["queue-a", 10, Leaser.ToString(), LeaseDuration], parameters);
    }

    [Fact]
    public void MySql_AllStatements_UseTimestampsWithFractionsOfASecond()
    {
        var dialect = new MySqlDialect();

        string[] statements =
        [
            dialect.GetLockJobsSql("queue-a", 10).Sql,
            dialect.GetLeaseJobsSql(Leaser, Jobs, LeaseDuration).Sql,
            dialect.GetExtendLeaseSql(Leaser, Jobs, LeaseDuration).Sql,
        ];

        Assert.All(statements, sql =>
        {
            Assert.Contains("UTC_TIMESTAMP(6)", sql);
            Assert.DoesNotMatch(@"UTC_TIMESTAMP(?!\(6\))", sql);
        });
    }

    [Fact]
    public void MySql_ClaimLocksRowsOldestFirst_ThenLeasesThemByPrimaryKey()
    {
        var dialect = new MySqlDialect();

        var (lockSql, lockParameters) = dialect.GetLockJobsSql("queue-a", 10);

        AssertFullyParameterized(lockSql, lockParameters);
        Assert.Contains("ORDER BY scheduled_at", lockSql);
        Assert.Contains("LIMIT {1}", lockSql);
        Assert.Contains("FOR UPDATE SKIP LOCKED", lockSql);
        Assert.DoesNotContain("leaser", lockSql);
        Assert.Equal(["queue-a", 10], lockParameters);

        var (leaseSql, parameters) = dialect.GetLeaseJobsSql(Leaser, Jobs, LeaseDuration);

        AssertFullyParameterized(leaseSql, parameters);
        Assert.Contains("queue = {2} AND scheduled_at = {3} AND id = {4}", leaseSql);
        Assert.Equal([Leaser.ToString(), LeaseDuration, "queue-a", ScheduledAt, 11L, "queue-b", ScheduledAt, 22L], parameters);
    }

    [Fact]
    public void Oracle_ClaimLocksAtMostMaxJobsRowsOldestFirst_ThenLeasesThem()
    {
        var (sql, parameters) = new OracleDialect().GetFetchJobsSql("queue-a", 10, Leaser, LeaseDuration);

        AssertFullyParameterized(sql, parameters);
        Assert.Contains("SELECT /*+ INDEX_ASC(XACT_JOBS_JOB (QUEUE, SCHEDULED_AT, ID)) */ ROWIDTOCHAR(ROWID)", sql);
        Assert.Contains("ORDER BY SCHEDULED_AT\n    FOR UPDATE SKIP LOCKED", sql);
        Assert.DoesNotContain("ROWNUM", sql); // applied before locked rows are skipped
        Assert.Contains("FETCH claimable BULK COLLECT INTO claimed LIMIT {1};\n  CLOSE claimable;", sql);
        Assert.Contains("SET LEASER = HEXTORAW({2})", sql);
        Assert.Contains("NUMTODSINTERVAL({3}, 'SECOND')", sql);
        Assert.Contains("DBMS_SQL.RETURN_RESULT(result);", sql);
        Assert.Equal(["queue-a", 10, Leaser.ToString("N"), LeaseDuration], parameters);

        // the result is opened before the lease, so it returns the rows as they were locked
        Assert.True(sql.IndexOf("OPEN result FOR", StringComparison.Ordinal) < sql.IndexOf("FORALL", StringComparison.Ordinal));
    }

    [Fact]
    public void Oracle_ContinuationClaimLocksAtMostMaxJobsRowsInIdOrder_ThenLeasesThem()
    {
        var (sql, parameters) = new OracleDialect().GetFetchContinuationsSql("queue-a", 10, Leaser, LeaseDuration, ClaimedAt);

        AssertFullyParameterized(sql, parameters);
        Assert.Contains("SELECT /*+ INDEX_ASC(c (QUEUE, SCHEDULED_AT, ID)) */ ROWIDTOCHAR(c.ROWID)", sql);
        Assert.Contains("ORDER BY c.ID\n    FOR UPDATE SKIP LOCKED", sql);
        Assert.DoesNotContain("ROWNUM", sql);
        Assert.Contains("BULK COLLECT INTO claimed LIMIT {2}", sql);
        Assert.Contains("SET LEASER = HEXTORAW({3})", sql);
        Assert.Contains("NUMTODSINTERVAL({4}, 'SECOND'),\n        ORIGINALLY_SCHEDULED_AT = COALESCE(ORIGINALLY_SCHEDULED_AT, {5})\n    WHERE ROWID", sql);
        Assert.Equal(["queue-a", XactJobBase.AwaitingBatchScheduledAt, 10, Leaser.ToString("N"), LeaseDuration, ClaimedAt], parameters);
    }

    [Fact]
    public void Oracle_ClaimLimit_FitsTheCollectionOfClaimedRows()
    {
        var (_, parameters) = new OracleDialect().GetFetchJobsSql("queue-a", int.MaxValue, Leaser, LeaseDuration);

        Assert.Equal(32767, parameters[1]); // SYS.ODCIVARCHAR2LIST is a VARRAY(32767)
    }

    // the continuation claim saves the claim time as the original time, if it isn't set yet, in the statement that leases the rows
    public static TheoryData<string, string> ContinuationClaims => new()
    {
        { "postgresql", "originally_scheduled_at = COALESCE(xact_jobs.job.originally_scheduled_at, {5})" },
        { "sqlserver", "[OriginallyScheduledAt] = COALESCE(target.[OriginallyScheduledAt], {5})" },
        { "sqlite", "originally_scheduled_at = COALESCE(originally_scheduled_at, {5})" },
        { "mysql", "originally_scheduled_at = COALESCE(originally_scheduled_at, {2})" },
        { "oracle", "ORIGINALLY_SCHEDULED_AT = COALESCE(ORIGINALLY_SCHEDULED_AT, {5})" },
    };

    [Theory]
    [MemberData(nameof(ContinuationClaims))]
    public void ContinuationClaim_SetsTheOriginalTimeToTheClaimTime_IfItIsntSet(string dialect, string expectedSet)
    {
        var (sql, parameters) = dialect switch
        {
            "postgresql" => new PostgreSqlDialect().GetFetchContinuationsSql("queue-a", 10, Leaser, LeaseDuration, ClaimedAt),
            "sqlserver" => new SqlServerDialect().GetFetchContinuationsSql("queue-a", 10, Leaser, LeaseDuration, ClaimedAt),
            "sqlite" => new SqliteDialect().GetFetchContinuationsSql("queue-a", 10, Leaser, LeaseDuration, ClaimedAt),
            "mysql" => new MySqlDialect().GetLeaseJobsSql(Leaser, Jobs, LeaseDuration, ClaimedAt),
            "oracle" => new OracleDialect().GetFetchContinuationsSql("queue-a", 10, Leaser, LeaseDuration, ClaimedAt),
            _ => throw new ArgumentOutOfRangeException(nameof(dialect))
        };

        AssertFullyParameterized(sql, parameters);
        Assert.Contains(expectedSet, sql);

        var index = int.Parse(System.Text.RegularExpressions.Regex.Match(expectedSet, @"\{(\d+)\}").Groups[1].Value);

        Assert.Equal(ClaimedAt, parameters[index]);
    }

    [Fact]
    public void MySql_ContinuationLease_NumbersTheJobKeysAfterTheClaimTime()
    {
        var (sql, parameters) = new MySqlDialect().GetLeaseJobsSql(Leaser, Jobs, LeaseDuration, ClaimedAt);

        Assert.Contains("queue = {3} AND scheduled_at = {4} AND id = {5}", sql);
        Assert.Equal([Leaser.ToString(), LeaseDuration, ClaimedAt, "queue-a", ScheduledAt, 11L, "queue-b", ScheduledAt, 22L], parameters);
    }

    // the claims of jobs keep the column as it is: a job's scheduled time doesn't change between two claims of its row
    [Fact]
    public void JobClaims_DontSetTheOriginalTime()
    {
        string[] statements =
        [
            new PostgreSqlDialect().GetFetchJobsSql("queue-a", 10, Leaser, LeaseDuration).Sql,
            new SqlServerDialect().GetFetchJobsSql("queue-a", 10, Leaser, LeaseDuration).Sql,
            new SqliteDialect().GetFetchJobsSql("queue-a", 10, Leaser, LeaseDuration).Sql,
            new MySqlDialect().GetLockJobsSql("queue-a", 10).Sql,
            new MySqlDialect().GetLeaseJobsSql(Leaser, Jobs, LeaseDuration).Sql,
            new OracleDialect().GetFetchJobsSql("queue-a", 10, Leaser, LeaseDuration).Sql,
        ];

        Assert.All(statements, sql => Assert.DoesNotContain("originally", sql, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertFullyParameterized(string sql, object[] parameters)
    {
        Assert.DoesNotContain(Leaser.ToString(), sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Leaser.ToString("N"), sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("queue-a", sql);

        var placeholders = Regex.Matches(sql, @"\{(\d+)\}")
            .Select(m => int.Parse(m.Groups[1].Value))
            .ToList();

        Assert.Equal(Enumerable.Range(0, parameters.Length), placeholders);
    }
}
