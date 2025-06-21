using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XactJobs.Internal;

namespace XactJobs.EntityConfigurations
{
    public class XactJobHistoryEntityConfiguration : IEntityTypeConfiguration<XactJobHistory>
    {
        private readonly ISqlDialect _sqlDialect;
        private readonly bool _excludeFromMigrations;

        public XactJobHistoryEntityConfiguration(string? providerName, bool excludeFromMigrations = true)
        {
            _sqlDialect = providerName.ToSqlDialect();
            _excludeFromMigrations = excludeFromMigrations;
        }

        public void Configure(EntityTypeBuilder<XactJobHistory> builder)
        {
            builder.Metadata.SetIsTableExcludedFromMigrations(_excludeFromMigrations);

            if (_sqlDialect.HasSchemaSupport)
            {
                builder.ToTable(_sqlDialect.XactJobHistoryTable, _sqlDialect.XactJobSchema);
            }
            else
            {
                builder.ToTable($"{_sqlDialect.XactJobSchema}_{_sqlDialect.XactJobHistoryTable}");
            }

            builder.HasKey(x => new { x.Id, x.ProcessedAt }).HasName($"pk_{_sqlDialect.XactJobHistoryTable}");

            builder.Property(x => x.Id).HasColumnName(_sqlDialect.ColId)
                .ValueGeneratedNever();

            builder.Property(x => x.Status).HasColumnName(_sqlDialect.ColStatus);

            builder.Property(x => x.ScheduledAt).HasColumnName(_sqlDialect.ColScheduledAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.ProcessedAt).HasColumnName(_sqlDialect.ColProcessedAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.TypeName).HasColumnName(_sqlDialect.ColTypeName);
            builder.Property(x => x.MethodName).HasColumnName(_sqlDialect.ColMethodName);
            builder.Property(x => x.MethodArgs).HasColumnName(_sqlDialect.ColMethodArgs);
            builder.Property(x => x.Queue).HasColumnName(_sqlDialect.ColQueue);

            builder.Property(x => x.ErrorCount).HasColumnName(_sqlDialect.ColErrorCount);
            builder.Property(x => x.ErrorMessage).HasColumnName(_sqlDialect.ColErrorMessage);
            builder.Property(x => x.ErrorStackTrace).HasColumnName(_sqlDialect.ColErrorStackTrace);

            builder.Property(x => x.PeriodicJobId).HasColumnName(_sqlDialect.ColPeriodicJobId);
            builder.Property(x => x.CronExpression).HasColumnName(_sqlDialect.ColCronExpression);

            builder.HasIndex(x => x.ProcessedAt)
                .HasDatabaseName($"ix_{_sqlDialect.XactJobHistoryTable}_{_sqlDialect.ColProcessedAt}");
        }
    }
}
