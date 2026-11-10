// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

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

        /// <param name="schema">The schema of XactJobs' tables, or null for the default (PostgreSQL and SQL Server only).</param>
        public XactJobPeriodicEntityConfiguration(string? providerName, bool excludeFromMigrations = true, string? schema = null)
        {
            _sqlDialect = providerName.ToSqlDialect(schema);
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
            builder.Property(x => x.Version).HasColumnName(_sqlDialect.ColVersion);

            builder.Property(x => x.State).HasColumnName(_sqlDialect.ColState)
                .HasMaxLength(XactJobPeriodic.MaxStateLength);

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
