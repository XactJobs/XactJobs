using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace XactJobs.Tests;

/// <summary>
/// The model's unique index of the periodic job column and its version, which EF migrations create, matches the SQL scripts on every
/// provider. The model is built without connecting.
/// </summary>
public class ModelIndexTests
{
    [Fact]
    public void PostgreSql_PartialIndex()
    {
        var index = GetPeriodicJobIndex(o => o.UseNpgsql("Host=localhost"));

        Assert.Equal("ix_job_periodic_job_id_version", index.GetDatabaseName());
        Assert.Equal("periodic_job_id IS NOT NULL", index.GetFilter());
    }

    [Fact]
    public void SqlServer_FilteredIndex_OnAnIndexableColumn()
    {
        var index = GetPeriodicJobIndex(o => o.UseSqlServer("Server=localhost"));

        Assert.Equal("IX_Job_PeriodicJobId_Version", index.GetDatabaseName());
        Assert.Equal("[PeriodicJobId] IS NOT NULL", index.GetFilter());

        // like the script: nvarchar(max) can't be indexed
        Assert.Equal("nvarchar(450)", index.Properties[0].GetColumnType());
    }

    [Fact]
    public void Oracle_PlainIndex_WhichLeavesOutTheNulls()
    {
        var index = GetPeriodicJobIndex(o => o.UseOracle("User Id=x;Password=x;Data Source=localhost"));

        Assert.Equal("IX_JOB_PERIODIC_JOB_ID_VERSION", index.GetDatabaseName());
        Assert.Null(index.GetFilter());
    }

    [Fact]
    public void MySql_PlainIndex_OnAVarchar()
    {
        AssertMySql(GetPeriodicJobIndex(o => o.UseMySQL("Server=localhost")));

#if !NET10_0_OR_GREATER // Pomelo doesn't support EF Core 10 yet
        AssertMySql(GetPeriodicJobIndex(o => o.UseMySql("Server=localhost", new MySqlServerVersion(new Version(8, 0, 36)))));
#endif

        static void AssertMySql(IIndex index)
        {
            Assert.Equal("ix_job_periodic_job_id_version", index.GetDatabaseName());
            Assert.Null(index.GetFilter());

            // like the script: a longtext can't be indexed
            Assert.Equal(255, index.Properties[0].GetMaxLength());
        }
    }

    [Fact]
    public void Sqlite_PartialIndex()
    {
        var index = GetPeriodicJobIndex(o => o.UseSqlite("Data Source=:memory:"));

        Assert.Equal("ix_job_periodic_job_id_version", index.GetDatabaseName());
        Assert.Equal("\"periodic_job_id\" IS NOT NULL", index.GetFilter());
    }

    private static IIndex GetPeriodicJobIndex(Action<DbContextOptionsBuilder<TestDbContext>> configure)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>();

        configure(options);

        using var db = new TestDbContext(options.Options);

        // the design-time model has the column types the migrations use
        var model = db.GetService<IDesignTimeModel>().Model;

        var index = Assert.Single(model.FindEntityType(typeof(XactJob))!.GetIndexes(), i => i.Properties[0].Name == nameof(XactJob.PeriodicJobId));

        Assert.Equal([nameof(XactJob.PeriodicJobId), nameof(XactJob.PeriodicJobVersion)], index.Properties.Select(p => p.Name));
        Assert.True(index.IsUnique);

        return index;
    }
}
