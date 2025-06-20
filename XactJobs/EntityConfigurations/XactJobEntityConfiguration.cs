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
                builder.ToTable(Names.XactJobTable, _sqlDialect.SchemaName);
            }
            else
            {
                builder.ToTable($"{_sqlDialect.SchemaName}__{Names.XactJobTable}");
            }

            builder.HasKey(x => new { x.Id }).HasName($"pk_{Names.XactJobTable}");

            builder.Property(x => x.Id).HasColumnName(Names.ColId);

            builder.Property(x => x.Status).HasColumnName(Names.ColStatus);

            builder.Property(x => x.LeasedUntil).HasColumnName(Names.ColLeasedUntil)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.ScheduledAt).HasColumnName(Names.ColScheduledAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.Leaser).HasColumnName(Names.ColLeaser);
            builder.Property(x => x.TypeName).HasColumnName(Names.ColTypeName);
            builder.Property(x => x.MethodName).HasColumnName(Names.ColMethodName);
            builder.Property(x => x.MethodArgs).HasColumnName(Names.ColMethodArgs);
            builder.Property(x => x.Queue).HasColumnName(Names.ColQueue);

            builder.Property(x => x.PeriodicJobId).HasColumnName(Names.ColPeriodicJobId);

            builder.Property(x => x.ErrorTime).HasColumnName(Names.ColErrorTime)
                .HasColumnType(_sqlDialect.DateTimeColumnType);
            builder.Property(x => x.ErrorCount).HasColumnName(Names.ColErrorCount);
            builder.Property(x => x.ErrorMessage).HasColumnName(Names.ColErrorMessage);
            builder.Property(x => x.ErrorStackTrace).HasColumnName(Names.ColErrorStackTrace);

            builder.HasIndex(x => new { x.Queue, x.ScheduledAt })
                .HasDatabaseName($"ix_{Names.XactJobTable}_{Names.ColQueue}_{Names.ColScheduledAt}");

            builder.HasIndex(x => x.PeriodicJobId)
                .HasDatabaseName($"ix_{Names.XactJobTable}_{Names.ColPeriodicJobId}");
        }
    }
}
