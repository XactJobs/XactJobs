// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

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

        /// <param name="schema">The schema of XactJobs' tables, or null for the default (PostgreSQL and SQL Server only).</param>
        public XactJobHistoryEntityConfiguration(string? providerName, bool excludeFromMigrations = true, string? schema = null)
        {
            _sqlDialect = providerName.ToSqlDialect(schema);
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

            // a job keeps its id across its retries, so it has a history row per attempt
            builder.HasKey(x => new { x.Id, x.ProcessedAt })
                .HasName($"{_sqlDialect.PrimaryKeyPrefix}_{_sqlDialect.XactJobHistoryTable}");

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

            if (_sqlDialect is OracleDialect)
            {
                // the default is NVARCHAR2(2000), and a failure that doesn't fit can't be recorded
                builder.Property(x => x.ErrorMessage).HasColumnType("NCLOB");
                builder.Property(x => x.ErrorStackTrace).HasColumnType("NCLOB");
            }

            builder.Property(x => x.BatchId).HasColumnName(_sqlDialect.ColBatchId);
            builder.Property(x => x.AwaitsBatchId).HasColumnName(_sqlDialect.ColAwaitsBatchId);
            builder.Property(x => x.ContinuationCondition).HasColumnName(_sqlDialect.ColContinuationCondition);

            builder.Property(x => x.OriginallyScheduledAt).HasColumnName(_sqlDialect.ColOriginallyScheduledAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.TraceParent).HasColumnName(_sqlDialect.ColTraceParent);

            builder.Property(x => x.PreviousState).HasColumnName(_sqlDialect.ColPreviousState)
                .HasMaxLength(XactJobPeriodic.MaxStateLength);
            builder.Property(x => x.State).HasColumnName(_sqlDialect.ColState)
                .HasMaxLength(XactJobPeriodic.MaxStateLength);

            builder.HasIndex(x => x.ProcessedAt)
                .HasDatabaseName($"{_sqlDialect.IndexPrefix}_{_sqlDialect.XactJobHistoryTable}_{_sqlDialect.ColProcessedAt}");

            // to count the failed jobs of a batch. Only the jobs of batches
            builder.HasIndex(x => x.BatchId)
                .HasDatabaseName($"{_sqlDialect.IndexPrefix}_{_sqlDialect.XactJobHistoryTable}_{_sqlDialect.ColBatchId}")
                .WhereNotNull(_sqlDialect, _sqlDialect.ColBatchId);

            // to find the continuations of a batch in the history. Only the continuations
            builder.HasIndex(x => x.AwaitsBatchId)
                .HasDatabaseName($"{_sqlDialect.IndexPrefix}_{_sqlDialect.XactJobHistoryTable}_{_sqlDialect.ColAwaitsBatchId}")
                .WhereNotNull(_sqlDialect, _sqlDialect.ColAwaitsBatchId);
        }
    }
}
