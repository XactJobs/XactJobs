// This file is part of XactJobs.
//
// XactJobs is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// XactJobs is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XactJobs.Internal;
using XactJobs.Internal.SqlDialects;

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

            if (_sqlDialect is SqliteDialect)
            {
                // Sqlite only supports autoincrement for INTEGER PRIMARY KEY
                builder.HasKey(x => x.Id)
                    .HasName($"{_sqlDialect.PrimaryKeyPrefix}_{_sqlDialect.XactJobHistoryTable}");
            }
            else
            {
                builder.HasKey(x => new { x.Id, x.ProcessedAt })
                    .HasName($"{_sqlDialect.PrimaryKeyPrefix}_{_sqlDialect.XactJobHistoryTable}");
            }

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
            builder.Property(x => x.PeriodicJobVersion).HasColumnName(_sqlDialect.ColPeriodicJobVersion);

            builder.HasIndex(x => x.ProcessedAt)
                .HasDatabaseName($"{_sqlDialect.IndexPrefix}_{_sqlDialect.XactJobHistoryTable}_{_sqlDialect.ColProcessedAt}");
        }
    }
}
