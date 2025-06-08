using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XactJobs.EntityConfigurations
{
    public class XactJobEntityConfiguration : IEntityTypeConfiguration<XactJob>
    {
        private readonly ISqlDialect _sqlDialect;

        public XactJobEntityConfiguration(string? providerName)
        {
            _sqlDialect = providerName.ToSqlDialect();
        }

        public void Configure(EntityTypeBuilder<XactJob> builder)
        {
            builder.ToTable(Names.XactJobTable, Names.XactJobSchema);

            builder.HasKey(x => x.Id).HasName($"pk_{Names.XactJobTable}");

            builder.Property(x => x.Id).HasColumnName(Names.ColId);

            builder.Property(x => x.CreatedAt).HasColumnName(Names.ColCreatedAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.UpdatedAt).HasColumnName(Names.ColUpdatedAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.LeasedUntil).HasColumnName(Names.ColLeasedUntil)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.ScheduledAt).HasColumnName(Names.ColScheduledAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.Leaser).HasColumnName(Names.ColLeaser);
            builder.Property(x => x.TypeName).HasColumnName(Names.ColTypeName);
            builder.Property(x => x.MethodName).HasColumnName(Names.ColMethodName);
            builder.Property(x => x.MethodArgs).HasColumnName(Names.ColMethodArgs);
            builder.Property(x => x.Queue).HasColumnName(Names.ColQueue);

            builder.Property(x => x.ErrorCount).HasColumnName(Names.ColErrorCount);
            builder.Property(x => x.ErrorMessage).HasColumnName(Names.ColErrorMessage);
            builder.Property(x => x.ErrorStackTrace).HasColumnName(Names.ColErrorStackTrace);

            builder.HasIndex(x => new { x.Queue, x.Status, x.ScheduledAt })
                .HasDatabaseName($"ix_{Names.ColQueue}_{Names.ColStatus}_{Names.ColScheduledAt}");
        }
    }
}
