using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XactJobs.EntityConfigurations
{
    public class XactJobArchiveEntityConfiguration : IEntityTypeConfiguration<XactJobArchive>
    {
        private readonly ISqlDialect _sqlDialect;
        private readonly bool _excludeFromMigrations;

        public XactJobArchiveEntityConfiguration(string? providerName, bool excludeFromMigrations = true)
        {
            _sqlDialect = providerName.ToSqlDialect();
            _excludeFromMigrations = excludeFromMigrations;
        }

        public void Configure(EntityTypeBuilder<XactJobArchive> builder)
        {
            builder.Metadata.SetIsTableExcludedFromMigrations(_excludeFromMigrations);

            if (_sqlDialect.HasSchemaSupport)
            {
                builder.ToTable(_sqlDialect.XactJobArchiveTable, _sqlDialect.XactJobSchema);
            }
            else
            {
                builder.ToTable($"{_sqlDialect.XactJobSchema}_{_sqlDialect.XactJobArchiveTable}");
            }

            builder.HasKey(x => x.Id).HasName($"pk_{_sqlDialect.XactJobArchiveTable}");

            builder.Property(x => x.Id).HasColumnName(_sqlDialect.ColId);

            builder.Property(x => x.Status).HasColumnName(_sqlDialect.ColStatus);

            builder.Property(x => x.ScheduledAt).HasColumnName(_sqlDialect.ColScheduledAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.CompletedAt).HasColumnName(_sqlDialect.ColCompletedAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.TypeName).HasColumnName(_sqlDialect.ColTypeName);
            builder.Property(x => x.MethodName).HasColumnName(_sqlDialect.ColMethodName);
            builder.Property(x => x.MethodArgs).HasColumnName(_sqlDialect.ColMethodArgs);
            builder.Property(x => x.Queue).HasColumnName(_sqlDialect.ColQueue);

            builder.Property(x => x.ErrorTime).HasColumnName(_sqlDialect.ColErrorTime)
                .HasColumnType(_sqlDialect.DateTimeColumnType);
            builder.Property(x => x.ErrorCount).HasColumnName(_sqlDialect.ColErrorCount);
            builder.Property(x => x.ErrorMessage).HasColumnName(_sqlDialect.ColErrorMessage);
            builder.Property(x => x.ErrorStackTrace).HasColumnName(_sqlDialect.ColErrorStackTrace);

            builder.Property(x => x.PeriodicJobId).HasColumnName(_sqlDialect.ColPeriodicJobId);
            builder.Property(x => x.CronExpression).HasColumnName(_sqlDialect.ColCronExpression);
            builder.Property(x => x.PeriodicJobName).HasColumnName(_sqlDialect.ColPeriodicJobName);

            builder.HasIndex(x => x.CompletedAt)
                .HasDatabaseName($"ix_{_sqlDialect.XactJobArchiveTable}_{_sqlDialect.ColCompletedAt}");
        }
    }
}
