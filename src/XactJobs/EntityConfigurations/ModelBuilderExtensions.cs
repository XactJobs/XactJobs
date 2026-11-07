// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using XactJobs.EntityConfigurations;

namespace Microsoft.EntityFrameworkCore
{
    public static class ModelBuilderExtensions
    {
        /// <param name="schema">
        /// The schema of XactJobs' tables, on PostgreSQL and SQL Server: e.g. one per app, for apps that share a database.
        /// By default xact_jobs on PostgreSQL (lowercase only), XactJobs on SQL Server. The workers find it in the model.
        /// </param>
        public static void ApplyXactJobsConfigurations(this ModelBuilder modelBuilder, string? providerName, bool excludeFromMigrations = true, string? schema = null)
        {
            modelBuilder.ApplyConfiguration(new XactJobEntityConfiguration(providerName, excludeFromMigrations, schema));
            modelBuilder.ApplyConfiguration(new XactJobHistoryEntityConfiguration(providerName, excludeFromMigrations, schema));
            modelBuilder.ApplyConfiguration(new XactJobPeriodicEntityConfiguration(providerName, excludeFromMigrations, schema));
        }
    }
}
