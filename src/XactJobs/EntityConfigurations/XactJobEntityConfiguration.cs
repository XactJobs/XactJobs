using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XactJobs.EntityConfigurations
{
    public class XactJobEntityConfiguration : IEntityTypeConfiguration<XactJob>
    {
        private readonly ISqlDialect _sqlDialect;
        private readonly bool _excludeFromMigrations;

        public XactJobEntityConfiguration(string? providerName, bool excludeFromMigrations = true)
        {
            _sqlDialect = providerName.ToSqlDialect();
            _excludeFromMigrations = excludeFromMigrations;
        }

        public void Configure(EntityTypeBuilder<XactJob> builder)
        {
            builder.Metadata.SetIsTableExcludedFromMigrations(_excludeFromMigrations);

            if (_sqlDialect.HasSchemaSupport)
            {
                builder.ToTable(_sqlDialect.XactJobTable, _sqlDialect.XactJobSchema);
            }
            else
            {
                builder.ToTable($"{_sqlDialect.XactJobSchema}_{_sqlDialect.XactJobTable}");
            }

            builder.HasKey(x => new { x.Id }).HasName($"pk_{_sqlDialect.XactJobTable}");

            builder.Property(x => x.Id).HasColumnName(_sqlDialect.ColId);

            builder.Property(x => x.Status).HasColumnName(_sqlDialect.ColStatus);

            builder.Property(x => x.LeasedUntil).HasColumnName(_sqlDialect.ColLeasedUntil)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.ScheduledAt).HasColumnName(_sqlDialect.ColScheduledAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.Leaser).HasColumnName(_sqlDialect.ColLeaser);
            builder.Property(x => x.TypeName).HasColumnName(_sqlDialect.ColTypeName);
            builder.Property(x => x.MethodName).HasColumnName(_sqlDialect.ColMethodName);
            builder.Property(x => x.MethodArgs).HasColumnName(_sqlDialect.ColMethodArgs);
            builder.Property(x => x.Queue).HasColumnName(_sqlDialect.ColQueue);

            builder.Property(x => x.PeriodicJobId).HasColumnName(_sqlDialect.ColPeriodicJobId);

            builder.Property(x => x.ErrorTime).HasColumnName(_sqlDialect.ColErrorTime)
                .HasColumnType(_sqlDialect.DateTimeColumnType);
            builder.Property(x => x.ErrorCount).HasColumnName(_sqlDialect.ColErrorCount);
            builder.Property(x => x.ErrorMessage).HasColumnName(_sqlDialect.ColErrorMessage);
            builder.Property(x => x.ErrorStackTrace).HasColumnName(_sqlDialect.ColErrorStackTrace);

            builder.HasIndex(x => new { x.Queue, x.ScheduledAt })
                .HasDatabaseName($"ix_{_sqlDialect.XactJobTable}_{_sqlDialect.ColQueue}_{_sqlDialect.ColScheduledAt}");

            builder.HasIndex(x => x.PeriodicJobId)
                .HasDatabaseName($"ix_{_sqlDialect.XactJobTable}_{_sqlDialect.ColPeriodicJobId}");

            if (_sqlDialect.GetAcquireLeaseSql != null)
            {
                builder.HasIndex(x => new { x.Queue, x.Leaser, x.LeasedUntil })
                    .HasDatabaseName($"ix_{_sqlDialect.XactJobTable}_{_sqlDialect.ColQueue}_{_sqlDialect.ColLeaser}");
            }
        }
    }
}
