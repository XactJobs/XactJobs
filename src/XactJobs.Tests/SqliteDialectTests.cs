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
    public void GetAcquireLeaseSql_ReturnsNull()
    {
        // Act
        var sql = _dialect.GetAcquireLeaseSql(QueueNames.Default, 10, Guid.NewGuid(), 30);

        // Assert
        Assert.Null(sql);
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
        var sql = _dialect.GetFetchJobsSql(queueName, maxJobs, leaser, leaseDuration);

        // Assert
        Assert.NotNull(sql);
        Assert.Contains("WITH cte AS", sql);
        Assert.Contains("SELECT", sql);
        Assert.Contains("UPDATE", sql);
        Assert.Contains("RETURNING *", sql);
        Assert.Contains($"LIMIT {maxJobs}", sql);
        Assert.Contains($"'{queueName}'", sql);
        Assert.Contains($"'{leaser}'", sql);
        Assert.Contains("datetime('subsec')", sql);
        Assert.Contains("datetime('now'", sql);
    }

    [Fact]
    public void GetFetchJobsSql_WithDefaultQueue_UsesDefaultQueueName()
    {
        // Arrange
        var leaser = Guid.NewGuid();

        // Act
        var sql = _dialect.GetFetchJobsSql(null, 5, leaser, 30);

        // Assert
        Assert.Contains($"'{QueueNames.Default}'", sql);
    }

    [Fact]
    public void GetFetchJobsSql_FiltersOnScheduledTime()
    {
        // Arrange
        var leaser = Guid.NewGuid();

        // Act
        var sql = _dialect.GetFetchJobsSql(QueueNames.Default, 10, leaser, 30);

        // Assert
        Assert.Contains($"{_dialect.ColScheduledAt} <= datetime('subsec')", sql);
    }

    [Fact]
    public void GetFetchJobsSql_OrdersByScheduledAt()
    {
        // Arrange
        var leaser = Guid.NewGuid();

        // Act
        var sql = _dialect.GetFetchJobsSql(QueueNames.Default, 10, leaser, 30);

        // Assert
        Assert.Contains($"ORDER BY {_dialect.ColScheduledAt}", sql);
    }

    [Fact]
    public void GetExtendLeaseSql_ContainsCorrectStructure()
    {
        // Arrange
        var leaser = Guid.NewGuid();
        const int leaseDuration = 60;

        // Act
        var sql = _dialect.GetExtendLeaseSql(leaser, leaseDuration);

        // Assert
        Assert.NotNull(sql);
        Assert.Contains("UPDATE", sql);
        Assert.Contains($"{_dialect.XactJobSchema}_{_dialect.XactJobTable}", sql);
        Assert.Contains($"SET {_dialect.ColLeasedUntil} = datetime('now', '+{leaseDuration} seconds')", sql);
        Assert.Contains($"WHERE {_dialect.ColLeaser} = '{leaser}'", sql);
    }

    [Fact]
    public void GetExtendLeaseSql_UsesCorrectLeaseDuration()
    {
        // Arrange
        var leaser = Guid.NewGuid();
        const int leaseDuration = 120;

        // Act
        var sql = _dialect.GetExtendLeaseSql(leaser, leaseDuration);

        // Assert
        Assert.Contains($"+{leaseDuration} seconds", sql);
    }

    [Fact]
    public void GetClearLeaseSql_ContainsCorrectStructure()
    {
        // Arrange
        var leaser = Guid.NewGuid();

        // Act
        var sql = _dialect.GetClearLeaseSql(leaser);

        // Assert
        Assert.NotNull(sql);
        Assert.Contains("UPDATE", sql);
        Assert.Contains($"{_dialect.XactJobSchema}_{_dialect.XactJobTable}", sql);
        Assert.Contains($"SET {_dialect.ColLeaser} = NULL, {_dialect.ColLeasedUntil} = NULL", sql);
        Assert.Contains($"WHERE {_dialect.ColLeaser} = '{leaser}'", sql);
    }

    [Fact]
    public void GetClearLeaseSql_SetsLeaserAndLeasedUntilToNull()
    {
        // Arrange
        var leaser = Guid.NewGuid();

        // Act
        var sql = _dialect.GetClearLeaseSql(leaser);

        // Assert
        Assert.Contains($"{_dialect.ColLeaser} = NULL", sql);
        Assert.Contains($"{_dialect.ColLeasedUntil} = NULL", sql);
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
    public void GetFetchJobsSql_WithDifferentMaxJobs_ReturnsCorrectLimit()
    {
        // Arrange
        var leaser = Guid.NewGuid();
        var testCases = new[] { 1, 5, 10, 50, 100 };

        foreach (var maxJobs in testCases)
        {
            // Act
            var sql = _dialect.GetFetchJobsSql(QueueNames.Default, maxJobs, leaser, 30);

            // Assert
            Assert.Contains($"LIMIT {maxJobs}", sql);
        }
    }

    [Fact]
    public void GetFetchJobsSql_ChecksLeasedUntilIsNullOrExpired()
    {
        // Arrange
        var leaser = Guid.NewGuid();

        // Act
        var sql = _dialect.GetFetchJobsSql(QueueNames.Default, 10, leaser, 30);

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
