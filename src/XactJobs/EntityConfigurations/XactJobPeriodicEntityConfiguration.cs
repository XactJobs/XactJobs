﻿// This file is part of XactJobs.
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
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using XactJobs.Internal;
using XactJobs.Internal.SqlDialects;

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

            if (_sqlDialect.HasSchemaSupport)
            {
                builder.ToTable(_sqlDialect.XactJobPeriodicTable, _sqlDialect.XactJobSchema);
            }
            else
            {
                builder.ToTable($"{_sqlDialect.XactJobSchema}_{_sqlDialect.XactJobPeriodicTable}");
            }

            builder.HasKey(x => x.Id)
                .HasName($"{_sqlDialect.PrimaryKeyPrefix}_{_sqlDialect.XactJobPeriodicTable}");

            builder.Property(x => x.Id).HasColumnName(_sqlDialect.ColId);

            builder.Property(x => x.CreatedAt).HasColumnName(_sqlDialect.ColCreatedAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.UpdatedAt).HasColumnName(_sqlDialect.ColUpdatedAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.CronExpression).HasColumnName(_sqlDialect.ColCronExpression);

            builder.Property(x => x.TypeName).HasColumnName(_sqlDialect.ColTypeName);
            builder.Property(x => x.MethodName).HasColumnName(_sqlDialect.ColMethodName);
            builder.Property(x => x.MethodArgs).HasColumnName(_sqlDialect.ColMethodArgs);
            builder.Property(x => x.Queue).HasColumnName(_sqlDialect.ColQueue);

            builder.Property(x => x.IsActive).HasColumnName(_sqlDialect.ColIsActive);

            if (_sqlDialect is OracleDialect)
            {

                builder.Property(x => x.IsActive)
                    .HasColumnType("NUMBER(1)")
                    .HasConversion(
                    new ValueConverter<bool, int>(
                        v => v ? 1 : 0,
                        v => v == 1
                    ));
            }
        }
    }
}
