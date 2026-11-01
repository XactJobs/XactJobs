using System.Text.RegularExpressions;
using XactJobs.Internal.SqlDialects;

namespace XactJobs.Tests;

public class SqlDialectLeaseSqlTests
{
    private const int LeaseDuration = 45;

    private static readonly Guid Leaser = Guid.NewGuid();
    private static readonly DateTime ScheduledAt = new(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);

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
