// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

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
        private readonly bool _isOfficialMySqlProvider;

        /// <param name="schema">The schema of XactJobs' tables, or null for the default (PostgreSQL and SQL Server only).</param>
        public XactJobEntityConfiguration(string? providerName, bool excludeFromMigrations = true, string? schema = null)
        {
            _sqlDialect = providerName.ToSqlDialect(schema);
            _isOfficialMySqlProvider = providerName == "MySql.EntityFrameworkCore";
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

            if (_sqlDialect is SqliteDialect)
            {
                // Sqlite only supports autoincrement for INTEGER PRIMARY KEY
                builder.HasKey(x => x.Id)
                    .HasName($"{_sqlDialect.PrimaryKeyPrefix}_{_sqlDialect.XactJobTable}");

                // the claims look for a queue's due jobs, oldest first: the other databases use their primary key for it,
                // without this index every claim would read and sort the whole table
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

            builder.Property(x => x.BatchId).HasColumnName(_sqlDialect.ColBatchId);
            builder.Property(x => x.AwaitsBatchId).HasColumnName(_sqlDialect.ColAwaitsBatchId);
            builder.Property(x => x.ContinuationCondition).HasColumnName(_sqlDialect.ColContinuationCondition);

            builder.Property(x => x.OriginallyScheduledAt).HasColumnName(_sqlDialect.ColOriginallyScheduledAt)
                .HasColumnType(_sqlDialect.DateTimeColumnType);

            builder.Property(x => x.TraceParent).HasColumnName(_sqlDialect.ColTraceParent);

            // to check if a batch has jobs left. Only the jobs of batches
            builder.HasIndex(x => x.BatchId)
                .HasDatabaseName($"{_sqlDialect.IndexPrefix}_{_sqlDialect.XactJobTable}_{_sqlDialect.ColBatchId}")
                .WhereNotNull(_sqlDialect, _sqlDialect.ColBatchId);

            // to find the continuations of a batch. Only the continuations
            builder.HasIndex(x => x.AwaitsBatchId)
                .HasDatabaseName($"{_sqlDialect.IndexPrefix}_{_sqlDialect.XactJobTable}_{_sqlDialect.ColAwaitsBatchId}")
                .WhereNotNull(_sqlDialect, _sqlDialect.ColAwaitsBatchId);

            ConfigurePeriodicJobIdIndex(builder);

            if (_isOfficialMySqlProvider)
            {
                // MySQL requires a key starting with an auto increment column, and it's not the first one of the primary key.
                // Pomelo adds this key on its own, and the SQL scripts have it too. It has to be part of CREATE TABLE, so it can't be an index.
                builder.HasAlternateKey(x => x.Id).HasName(_sqlDialect.ColId);
            }

            if (_sqlDialect is PostgreSqlDialect)
            {
                // Free space on each page, so that lease updates can be HOT updates.
                // This is the annotation behind Npgsql's HasFillFactor (this library doesn't reference Npgsql).
                builder.HasAnnotation("Npgsql:StorageParameter:fillfactor", 70);
            }
        }

        /// <summary>
        /// To find the runs of a periodic job (e.g. when it's paused, or ensured at start), without reading the whole table.
        /// Only the rows of periodic jobs, one pending run each: the other jobs' inserts and deletes don't write to it.
        /// </summary>
        private void ConfigurePeriodicJobIdIndex(EntityTypeBuilder<XactJob> builder)
        {
            // MySQL has no filtered indexes, and a full one would be written by every job
            if (_sqlDialect is MySqlDialect) return;

            builder.HasIndex(x => x.PeriodicJobId)
                .HasDatabaseName($"{_sqlDialect.IndexPrefix}_{_sqlDialect.XactJobTable}_{_sqlDialect.ColPeriodicJobId}")
                .WhereNotNull(_sqlDialect, _sqlDialect.ColPeriodicJobId);
        }
    }
}
