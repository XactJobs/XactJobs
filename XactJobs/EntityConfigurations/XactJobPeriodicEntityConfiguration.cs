using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

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

            builder.ToTable(Names.XactJobPeriodicTable, Names.XactJobSchema);

            builder.HasKey(x => x.Id).HasName($"pk_{Names.XactJobPeriodicTable}");

            builder.Property(x => x.Id).HasColumnName(Names.ColId);

            builder.Property(x => x.CreatedAt).HasColumnName(Names.ColCreatedAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.CronExpression).HasColumnName(Names.ColCronExpression);

            builder.Property(x => x.TypeName).HasColumnName(Names.ColTypeName);
            builder.Property(x => x.MethodName).HasColumnName(Names.ColMethodName);
            builder.Property(x => x.MethodArgs).HasColumnName(Names.ColMethodArgs);
            builder.Property(x => x.Queue).HasColumnName(Names.ColQueue);

            builder.Property(x => x.LastJobId).HasColumnName(Names.ColLastJobId);
        }
    }
}
