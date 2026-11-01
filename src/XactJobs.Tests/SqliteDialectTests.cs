using Microsoft.EntityFrameworkCore;
using XactJobs.Internal.SqlDialects;

namespace XactJobs.Tests;

public class SqliteDialectTests
{
    private readonly SqliteDialect _dialect;

    public SqliteDialectTests()
    {
        _dialect = new SqliteDialect();
    }

    [Fact]
    public void SqliteDialect_HasCorrectTableNames()
    {
        // Assert
        Assert.Equal("xact_jobs", _dialect.XactJobSchema);
        Assert.Equal("job", _dialect.XactJobTable);
        Assert.Equal("job_history", _dialect.XactJobHistoryTable);
        Assert.Equal("job_periodic", _dialect.XactJobPeriodicTable);
    }

    [Fact]
    public void SqliteDialect_HasCorrectPrefixes()
    {
        // Assert
        Assert.Equal("pk", _dialect.PrimaryKeyPrefix);
        Assert.Equal("ix", _dialect.IndexPrefix);
    }

    [Fact]
    public void SqliteDialect_HasCorrectColumnNames()
    {
        // Assert
        Assert.Equal("id", _dialect.ColId);
        Assert.Equal("created_at", _dialect.ColCreatedAt);
        Assert.Equal("scheduled_at", _dialect.ColScheduledAt);
        Assert.Equal("processed_at", _dialect.ColProcessedAt);
        Assert.Equal("leased_until", _dialect.ColLeasedUntil);
        Assert.Equal("leaser", _dialect.ColLeaser);
        Assert.Equal("type_name", _dialect.ColTypeName);
        Assert.Equal("method_name", _dialect.ColMethodName);
        Assert.Equal("method_args", _dialect.ColMethodArgs);
        Assert.Equal("status", _dialect.ColStatus);
        Assert.Equal("queue", _dialect.ColQueue);
        Assert.Equal("periodic_job_id", _dialect.ColPeriodicJobId);
        Assert.Equal("periodic_job_version", _dialect.ColPeriodicJobVersion);
        Assert.Equal("error_count", _dialect.ColErrorCount);
        Assert.Equal("error_time", _dialect.ColErrorTime);
        Assert.Equal("error_message", _dialect.ColErrorMessage);
        Assert.Equal("error_stack_trace", _dialect.ColErrorStackTrace);
        Assert.Equal("cron_expression", _dialect.ColCronExpression);
        Assert.Equal("updated_at", _dialect.ColUpdatedAt);
        Assert.Equal("is_active", _dialect.ColIsActive);
        Assert.Equal("version", _dialect.ColVersion);
    }

    [Fact]
    public void SqliteDialect_HasNoSchemaSupport()
    {
        // Assert
        Assert.False(_dialect.HasSchemaSupport);
    }

    [Fact]
    public void SqliteDialect_HasCorrectDateTimeColumnType()
    {
        // Assert
        Assert.Equal("DATETIME", _dialect.DateTimeColumnType);
    }

    [Fact]
    public void JobTable_HasNoLeaserIndex()
    {
        // Arrange
        using var context = CreateTestContext();

        // Act
        var indexes = context.Model.FindEntityType(typeof(XactJob))!.GetIndexes();

        // Assert
        Assert.Empty(indexes);
    }

    [Fact]
    public void GetFetchJobsSql_ContainsCorrectStructure()
    {
        // Arrange
        var leaser = Guid.NewGuid();
        const string queueName = "test-queue";
        const int maxJobs = 10;
        const int leaseDuration = 30;

        // Act
        var (sql, parameters) = _dialect.GetFetchJobsSql(queueName, maxJobs, leaser, leaseDuration);

        // Assert
        Assert.Contains("WITH cte AS", sql);
        Assert.Contains("SELECT", sql);
        Assert.Contains("UPDATE", sql);
        Assert.Contains("RETURNING *", sql);
        Assert.Contains($"{_dialect.ColQueue} = {{0}}", sql);
        Assert.Contains("LIMIT {1}", sql);
        Assert.Contains($"SET {_dialect.ColLeaser} = {{2}}", sql);
        Assert.Contains("datetime('subsec', '+' || {3} || ' seconds')", sql);
        Assert.Equal([queueName, maxJobs, leaser.ToString(), leaseDuration], parameters);
    }

    [Fact]
    public void GetFetchJobsSql_WithDefaultQueue_UsesDefaultQueueName()
    {
        // Arrange
        var leaser = Guid.NewGuid();

        // Act
        var (_, parameters) = _dialect.GetFetchJobsSql(null, 5, leaser, 30);

        // Assert
        Assert.Equal(QueueNames.Default, parameters[0]);
    }

    [Fact]
    public void GetFetchJobsSql_FiltersOnScheduledTime()
    {
        // Arrange
        var leaser = Guid.NewGuid();

        // Act
        var (sql, _) = _dialect.GetFetchJobsSql(QueueNames.Default, 10, leaser, 30);

        // Assert
        Assert.Contains($"{_dialect.ColScheduledAt} <= datetime('subsec')", sql);
    }

    [Fact]
    public void GetFetchJobsSql_OrdersByScheduledAt()
    {
        // Arrange
        var leaser = Guid.NewGuid();

        // Act
        var (sql, _) = _dialect.GetFetchJobsSql(QueueNames.Default, 10, leaser, 30);

        // Assert
        Assert.Contains($"ORDER BY {_dialect.ColScheduledAt}", sql);
    }

    [Fact]
    public void GetExtendLeaseSql_ContainsCorrectStructure()
    {
        // Arrange
        var leaser = Guid.NewGuid();
        const int leaseDuration = 60;
        XactJob[] jobs = [CreateJob(7), CreateJob(9)];

        // Act
        var (sql, parameters) = _dialect.GetExtendLeaseSql(leaser, jobs, leaseDuration);

        // Assert
        Assert.Contains("UPDATE", sql);
        Assert.Contains($"{_dialect.XactJobSchema}_{_dialect.XactJobTable}", sql);
        Assert.Contains($"SET {_dialect.ColLeasedUntil} = datetime('subsec', '+' || {{0}} || ' seconds')", sql);
        Assert.Contains($"WHERE {_dialect.ColLeaser} = {{1}}", sql);
        Assert.Contains($"{_dialect.ColId} IN ({{2}}, {{3}})", sql);
        Assert.Equal([leaseDuration, leaser.ToString(), 7L, 9L], parameters);
    }

    [Fact]
    public void GetClearLeaseSql_ContainsCorrectStructure()
    {
        // Arrange
        var leaser = Guid.NewGuid();
        XactJob[] jobs = [CreateJob(7), CreateJob(9)];

        // Act
        var (sql, parameters) = _dialect.GetClearLeaseSql(leaser, jobs);

        // Assert
        Assert.Contains("UPDATE", sql);
        Assert.Contains($"{_dialect.XactJobSchema}_{_dialect.XactJobTable}", sql);
        Assert.Contains($"SET {_dialect.ColLeaser} = NULL, {_dialect.ColLeasedUntil} = NULL", sql);
        Assert.Contains($"WHERE {_dialect.ColLeaser} = {{0}}", sql);
        Assert.Contains($"{_dialect.ColId} IN ({{1}}, {{2}})", sql);
        Assert.Equal([leaser.ToString(), 7L, 9L], parameters);
    }

    private static XactJob CreateJob(long id)
    {
        return new XactJob(id, DateTime.UtcNow, "Type", "Method", "[]", QueueNames.Default);
    }

    [Fact]
    public async Task AcquireTableLockAsync_CompletesSuccessfully()
    {
        // Arrange
        using var context = CreateTestContext();

        // Act & Assert
        await _dialect.AcquireTableLockAsync(context, "schema", "table", CancellationToken.None);
        // Should complete without throwing
    }

    [Fact]
    public async Task ReleaseTableLockAsync_CompletesSuccessfully()
    {
        // Arrange
        using var context = CreateTestContext();

        // Act & Assert
        await _dialect.ReleaseTableLockAsync(context, "schema", "table", CancellationToken.None);
        // Should complete without throwing
    }

    [Fact]
    public void GetFetchJobsSql_ChecksLeasedUntilIsNullOrExpired()
    {
        // Arrange
        var leaser = Guid.NewGuid();

        // Act
        var (sql, _) = _dialect.GetFetchJobsSql(QueueNames.Default, 10, leaser, 30);

        // Assert
        Assert.Contains($"({_dialect.ColLeasedUntil} IS NULL OR {_dialect.ColLeasedUntil} < datetime('subsec'))", sql);
    }

    private TestDbContext CreateTestContext()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        var context = new TestDbContext(options);
        context.Database.OpenConnection();
        context.Database.EnsureCreated();
        return context;
    }
}
