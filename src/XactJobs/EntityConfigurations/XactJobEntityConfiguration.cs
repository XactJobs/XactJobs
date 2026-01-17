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

            void tableBuilder(TableBuilder<XactJob> tb) 
            {
                // make sure every periodic job is valid (either both PeriodicJobId and PeriodicJobVersion are NULL or both are NOT NULL)
                tb.HasCheckConstraint($"{_sqlDialect.CheckConstraintPrefix}_{_sqlDialect.XactJobTable}_{_sqlDialect.ColPeriodicJobId}", 
                    _sqlDialect.GetPeriodicJobCheckConstraintSql());
            }

            if (_sqlDialect.HasSchemaSupport)
            {
                builder.ToTable(_sqlDialect.XactJobTable, _sqlDialect.XactJobSchema, tableBuilder);
            }
            else
            {
                builder.ToTable($"{_sqlDialect.XactJobSchema}_{_sqlDialect.XactJobTable}", tableBuilder);
            }

            if (_sqlDialect is SqliteDialect)
            {
                // Sqlite only supports autoincrement for INTEGER PRIMARY KEY
                builder.HasKey(x => x.Id)
                    .HasName($"{_sqlDialect.PrimaryKeyPrefix}_{_sqlDialect.XactJobTable}");

                builder.HasIndex(x => new { x.Queue, x.ScheduledAt })
                    .HasDatabaseName($"{_sqlDialect.IndexPrefix}_{_sqlDialect.XactJobTable}_{_sqlDialect.ColQueue}_{_sqlDialect.ColScheduledAt}");
            }
            else
            {
                builder.HasKey(x => new { x.Queue, x.ScheduledAt, x.Id })
                    .HasName($"{_sqlDialect.PrimaryKeyPrefix}_{_sqlDialect.XactJobTable}");
            }

            builder.Property(x => x.Id).HasColumnName(_sqlDialect.ColId)
                .ValueGeneratedOnAdd();

            builder.Property(x => x.LeasedUntil).HasColumnName(_sqlDialect.ColLeasedUntil)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.ScheduledAt).HasColumnName(_sqlDialect.ColScheduledAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.Leaser).HasColumnName(_sqlDialect.ColLeaser);
            builder.Property(x => x.TypeName).HasColumnName(_sqlDialect.ColTypeName);
            builder.Property(x => x.MethodName).HasColumnName(_sqlDialect.ColMethodName);
            builder.Property(x => x.MethodArgs).HasColumnName(_sqlDialect.ColMethodArgs);

            builder.Property(x => x.Queue).HasColumnName(_sqlDialect.ColQueue)
                .HasMaxLength(50); // to limit the max clustered index size

            builder.Property(x => x.PeriodicJobId).HasColumnName(_sqlDialect.ColPeriodicJobId);
            builder.Property(x => x.CronExpression).HasColumnName(_sqlDialect.ColCronExpression);
            builder.Property(x => x.PeriodicJobVersion).HasColumnName(_sqlDialect.ColPeriodicJobVersion);

            builder.Property(x => x.ErrorCount).HasColumnName(_sqlDialect.ColErrorCount);

            builder.HasIndex(x => x.Leaser)
                .HasDatabaseName($"{_sqlDialect.IndexPrefix}_{_sqlDialect.XactJobTable}_{_sqlDialect.ColLeaser}");

            builder.HasIndex(x => new { x.PeriodicJobId, x.PeriodicJobVersion })
                .IsUnique()
                .HasFilter(_sqlDialect.GetPeriodicJobUniqueIndexFilterSql())
                .HasDatabaseName($"{_sqlDialect.UniquePrefix}_{_sqlDialect.XactJobTable}_{_sqlDialect.ColPeriodicJobId}");
        }
    }
}
