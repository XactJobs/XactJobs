using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace XactJobs.EntityConfigurations
{
    internal class XactJobEntityConfiguration : IEntityTypeConfiguration<XactJob>
    {
        public void Configure(EntityTypeBuilder<XactJob> builder)
        {
            builder.ToTable(Names.TableXactJob);

            builder.HasKey(x => x.Id).HasName($"pk_{Names.TableXactJob}_{Names.ColId}");

            builder.Property(x => x.Id).HasColumnName(Names.ColId);
            builder.Property(x => x.CreatedAt).HasColumnName(Names.ColCreatedAt);
            builder.Property(x => x.UpdatedAt).HasColumnName(Names.ColUpdatedAt);
            builder.Property(x => x.ScheduledAt).HasColumnName(Names.ColScheduledAt);
            builder.Property(x => x.TypeName).HasColumnName(Names.ColTypeName);
            builder.Property(x => x.MethodName).HasColumnName(Names.ColMethodName);
            builder.Property(x => x.MethodArgs).HasColumnName(Names.ColMethodArgs);
            builder.Property(x => x.Queue).HasColumnName(Names.ColQueue);

            builder.Property(x => x.ErrorCount).HasColumnName(Names.ColErrorCount);
            builder.Property(x => x.ErrorMessage).HasColumnName(Names.ColErrorMessage);
            builder.Property(x => x.ErrorStackTrace).HasColumnName(Names.ColErrorStackTrace);
        }
    }
}
