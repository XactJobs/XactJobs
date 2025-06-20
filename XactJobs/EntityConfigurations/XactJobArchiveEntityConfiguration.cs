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

            builder.ToTable(Names.XactJobArchiveTable, Names.XactJobSchema);

            builder.HasKey(x => x.Id).HasName($"pk_{Names.XactJobArchiveTable}");

            builder.Property(x => x.Id).HasColumnName(Names.ColId);

            builder.Property(x => x.Status).HasColumnName(Names.ColStatus);

            builder.Property(x => x.ScheduledAt).HasColumnName(Names.ColScheduledAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.CompletedAt).HasColumnName(Names.ColCompletedAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.TypeName).HasColumnName(Names.ColTypeName);
            builder.Property(x => x.MethodName).HasColumnName(Names.ColMethodName);
            builder.Property(x => x.MethodArgs).HasColumnName(Names.ColMethodArgs);
            builder.Property(x => x.Queue).HasColumnName(Names.ColQueue);

            builder.Property(x => x.ErrorTime).HasColumnName(Names.ColErrorTime)
                .HasColumnType(_sqlDialect.DateTimeColumnType);
            builder.Property(x => x.ErrorCount).HasColumnName(Names.ColErrorCount);
            builder.Property(x => x.ErrorMessage).HasColumnName(Names.ColErrorMessage);
            builder.Property(x => x.ErrorStackTrace).HasColumnName(Names.ColErrorStackTrace);

            builder.Property(x => x.PeriodicJobId).HasColumnName(Names.ColPeriodicJobId);
            builder.Property(x => x.CronExpression).HasColumnName(Names.ColCronExpression);
            builder.Property(x => x.PeriodicJobName).HasColumnName(Names.ColPeriodicJobName);

            builder.HasIndex(x => x.CompletedAt)
                .HasDatabaseName($"ix_{Names.XactJobArchiveTable}_{Names.ColCompletedAt}");
        }
    }
}
