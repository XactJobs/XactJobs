using Microsoft.EntityFrameworkCore;
using XactJobs.Internal.SqlDialects;

namespace XactJobs.Tests;

/// <summary>
/// The schema of XactJobs' tables, for apps that share a PostgreSQL or SQL Server database (ApplyXactJobsConfigurations).
/// The model is built without connecting.
/// </summary>
public class ModelSchemaTests
{
    [Fact]
    public void PostgreSql_TheTablesAreInTheSchema_AndTheSqlUsesIt()
    {
        using var db = new SchemaDbContext(o => o.UseNpgsql("Host=localhost"), "app_b");

        Assert.All(XactJobsEntityTypes(db), entity => Assert.Equal("app_b", entity.GetSchema()));

        // the workers' dialect, with the model's schema (SchemaIntegrationTests runs them)
        var (sql, _) = new PostgreSqlDialect("app_b").GetFetchJobsSql(null, 10, Guid.NewGuid(), 60);

        Assert.Contains("FROM app_b.job", sql);
        Assert.DoesNotContain("xact_jobs.", sql);
    }

    [Fact]
    public void SqlServer_TheTablesAreInTheSchema_AndTheSqlUsesIt()
    {
        using var db = new SchemaDbContext(o => o.UseSqlServer("Server=localhost"), "AppB");

        Assert.All(XactJobsEntityTypes(db), entity => Assert.Equal("AppB", entity.GetSchema()));

        var (sql, _) = new SqlServerDialect("AppB").GetFetchJobsSql(null, 10, Guid.NewGuid(), 60);

        Assert.Contains("[AppB].[Job]", sql);
        Assert.DoesNotContain("[XactJobs].", sql);
    }

    [Fact]
    public void ByDefault_TheSchemaIsTheUsualOne()
    {
        using var postgres = new SchemaDbContext(o => o.UseNpgsql("Host=localhost"), null);
        using var sqlServer = new SchemaDbContext(o => o.UseSqlServer("Server=localhost"), null);

        Assert.All(XactJobsEntityTypes(postgres), entity => Assert.Equal("xact_jobs", entity.GetSchema()));
        Assert.All(XactJobsEntityTypes(sqlServer), entity => Assert.Equal("XactJobs", entity.GetSchema()));
    }

    [Fact]
    public void OtherDatabases_HaveNoSchema()
    {
        // each app has a database of its own there (on Oracle, a user)
        foreach (var configure in new Action<DbContextOptionsBuilder>[]
        {
            o => o.UseSqlite("Data Source=:memory:"),
            o => o.UseMySQL("Server=localhost"),
            o => o.UseOracle("User Id=x;Password=x;Data Source=localhost"),
        })
        {
            using var db = new SchemaDbContext(configure, "app_b");

            Assert.Throws<NotSupportedException>(() => db.Model);
        }
    }

    [Theory]
    [InlineData("App_B")]   // PostgreSQL folds the unquoted name to lowercase, while EF quotes it
    [InlineData("app-b")]
    [InlineData("app b; DROP TABLE x")]
    [InlineData("")]
    public void PostgreSql_OnlyTakesALowercasePlainName(string schema)
    {
        Assert.Throws<ArgumentException>(() => new PostgreSqlDialect(schema));
    }

    [Theory]
    [InlineData("App-B")]
    [InlineData("AppB]; DROP TABLE x --")]
    [InlineData("")]
    public void SqlServer_OnlyTakesAPlainName(string schema)
    {
        Assert.Throws<ArgumentException>(() => new SqlServerDialect(schema));
    }

    private static IEnumerable<Microsoft.EntityFrameworkCore.Metadata.IEntityType> XactJobsEntityTypes(DbContext db) =>
        [db.Model.FindEntityType(typeof(XactJob))!, db.Model.FindEntityType(typeof(XactJobHistory))!, db.Model.FindEntityType(typeof(XactJobPeriodic))!];

    /// <summary>
    /// A model per instance, so that each test has its own schema (EF caches a model per context type otherwise).
    /// </summary>
    private sealed class SchemaDbContext(Action<DbContextOptionsBuilder> configure, string? schema) : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            configure(optionsBuilder);
            optionsBuilder.ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, PerSchemaModelCacheKeyFactory>();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyXactJobsConfigurations(Database.ProviderName, excludeFromMigrations: false, schema: schema);
        }

        public string? Schema => schema;
    }

    private sealed class PerSchemaModelCacheKeyFactory : Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) => (context.GetType(), ((SchemaDbContext)context).Schema, context.Database.ProviderName, designTime);
    }
}
