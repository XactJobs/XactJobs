using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using XactJobs.SqlDialects;

namespace XactJobs.EntityConfigurations
{
    public class XactJobPeriodicEntityConfiguration : IEntityTypeConfiguration<XactJobPeriodic>
    {
        private readonly ISqlDialect _sqlDialect;
        private readonly bool _excludeFromMigrations;

        public XactJobPeriodicEntityConfiguration(string? providerName, bool excludeFromMigrations = true)
        {
            _sqlDialect = providerName.ToSqlDialect();
            _excludeFromMigrations = excludeFromMigrations;
        }

        public void Configure(EntityTypeBuilder<XactJobPeriodic> builder)
        {
            builder.Metadata.SetIsTableExcludedFromMigrations(_excludeFromMigrations);

            if (_sqlDialect.HasSchemaSupport)
            {
                builder.ToTable(Names.XactJobPeriodicTable, _sqlDialect.SchemaName);
            }
            else
            {
                builder.ToTable($"{_sqlDialect.SchemaName}__{Names.XactJobPeriodicTable}");
            }

            builder.HasKey(x => x.Id).HasName($"pk_{Names.XactJobPeriodicTable}");

            builder.Property(x => x.Id).HasColumnName(Names.ColId);

            builder.Property(x => x.Name).HasColumnName(Names.ColName);

            builder.Property(x => x.CreatedAt).HasColumnName(Names.ColCreatedAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.UpdatedAt).HasColumnName(Names.ColUpdatedAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.CronExpression).HasColumnName(Names.ColCronExpression);

            builder.Property(x => x.TypeName).HasColumnName(Names.ColTypeName);
            builder.Property(x => x.MethodName).HasColumnName(Names.ColMethodName);
            builder.Property(x => x.MethodArgs).HasColumnName(Names.ColMethodArgs);
            builder.Property(x => x.Queue).HasColumnName(Names.ColQueue);

            builder.Property(x => x.IsActive).HasColumnName(Names.ColIsActive);

            if (_sqlDialect is OracleDialect)
            {

                builder.Property(x => x.IsActive)
                    .HasColumnType("NUMBER(1)")
                    .HasConversion(
                    new ValueConverter<bool, int>(
                        v => v ? 1 : 0,
                        v => v == 1
                    ));
            }

            builder.HasIndex(x => x.Name)
                .HasDatabaseName($"ix_{Names.XactJobPeriodicTable}_{Names.ColName}")
                .IsUnique();
        }
    }
}
