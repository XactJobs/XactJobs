using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace XactJobs.Tests;

/// <summary>
/// The model's index of the periodic job column, which EF migrations create, matches the SQL scripts on every provider.
/// The model is built without connecting.
/// </summary>
public class ModelIndexTests
{
    [Fact]
    public void PostgreSql_PartialIndex()
    {
        var index = GetPeriodicJobIdIndex(o => o.UseNpgsql("Host=localhost"));

        Assert.NotNull(index);

        Assert.Equal("ix_job_periodic_job_id", index.GetDatabaseName());
        Assert.Equal("periodic_job_id IS NOT NULL", index.GetFilter());
    }

    [Fact]
    public void SqlServer_FilteredIndex_OnAnIndexableColumn()
    {
        var index = GetPeriodicJobIdIndex(o => o.UseSqlServer("Server=localhost"));

        Assert.NotNull(index);

        Assert.Equal("IX_Job_PeriodicJobId", index.GetDatabaseName());
        Assert.Equal("[PeriodicJobId] IS NOT NULL", index.GetFilter());

        // like the script: nvarchar(max) can't be indexed
        var column = index.Properties.Single();

        Assert.Equal("nvarchar(450)", column.GetColumnType());
    }

    [Fact]
    public void Oracle_PlainIndex_WhichLeavesOutTheNulls()
    {
        var index = GetPeriodicJobIdIndex(o => o.UseOracle("User Id=x;Password=x;Data Source=localhost"));

        Assert.NotNull(index);

        Assert.Equal("IX_JOB_PERIODIC_JOB_ID", index.GetDatabaseName());
        Assert.Null(index.GetFilter());
    }

    [Fact]
    public void MySql_NoIndex()
    {
        Assert.Null(GetPeriodicJobIdIndex(o => o.UseMySQL("Server=localhost")));

#if !NET10_0_OR_GREATER // Pomelo doesn't support EF Core 10 yet
        Assert.Null(GetPeriodicJobIdIndex(o => o.UseMySql("Server=localhost", new MySqlServerVersion(new Version(8, 0, 36)))));
#endif
    }

    [Fact]
    public void Sqlite_PartialIndex()
    {
        var index = GetPeriodicJobIdIndex(o => o.UseSqlite("Data Source=:memory:"));

        Assert.NotNull(index);

        Assert.Equal("ix_job_periodic_job_id", index.GetDatabaseName());
        Assert.Equal("\"periodic_job_id\" IS NOT NULL", index.GetFilter());
    }

    private static IIndex? GetPeriodicJobIdIndex(Action<DbContextOptionsBuilder<TestDbContext>> configure)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>();

        configure(options);

        using var db = new TestDbContext(options.Options);

        // the design-time model has the column types the migrations use
        var model = db.GetService<IDesignTimeModel>().Model;

        return model.FindEntityType(typeof(XactJob))!.GetIndexes()
            .SingleOrDefault(i => i.Properties.Count == 1 && i.Properties[0].Name == nameof(XactJob.PeriodicJobId));
    }
}
