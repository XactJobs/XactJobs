// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using XactJobs.Internal;
using XactJobs.Internal.SqlDialects;

namespace XactJobs.EntityConfigurations
{
    internal static class IndexFilters
    {
        /// <summary>
        /// Leaves out the rows where the column is null, for a column that's null in most rows (a job of no batch, no periodic job):
        /// those rows' inserts and deletes don't write to the index. A query uses it only if it implies that the column isn't
        /// null (an equality does). Oracle leaves those rows out on its own (it doesn't store the rows where all the index's
        /// columns are null), and MySQL has no filtered indexes.
        /// </summary>
        public static IndexBuilder<T> WhereNotNull<T>(this IndexBuilder<T> index, ISqlDialect dialect, string column)
        {
            var filter = dialect switch
            {
                PostgreSqlDialect => $"{column} IS NOT NULL",
                SqlServerDialect => $"[{column}] IS NOT NULL",
                SqliteDialect => $"\"{column}\" IS NOT NULL",
                _ => null
            };

            // also null explicitly: Oracle's provider gives a unique index of nullable columns an IS NOT NULL filter of its own
            return index.HasFilter(filter);
        }
    }
}
