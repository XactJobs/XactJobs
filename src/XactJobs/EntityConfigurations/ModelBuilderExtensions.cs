// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using XactJobs.EntityConfigurations;

namespace Microsoft.EntityFrameworkCore
{
    public static class ModelBuilderExtensions
    {
        /// <param name="includeInMigrations">
        /// True includes XactJobs' tables in your EF migrations (and in EnsureCreated). False (the default) when they are created with the SQL scripts.
        /// </param>
        /// <param name="schema">
        /// The schema of XactJobs' tables, on PostgreSQL and SQL Server: e.g. one per app, for apps that share a database.
        /// By default xact_jobs on PostgreSQL (lowercase only), XactJobs on SQL Server. The workers find it in the model.
        /// </param>
        public static void ApplyXactJobsConfigurations(this ModelBuilder modelBuilder, string? providerName, bool includeInMigrations = false, string? schema = null)
        {
            modelBuilder.ApplyConfiguration(new XactJobEntityConfiguration(providerName, includeInMigrations, schema));
            modelBuilder.ApplyConfiguration(new XactJobHistoryEntityConfiguration(providerName, includeInMigrations, schema));
            modelBuilder.ApplyConfiguration(new XactJobPeriodicEntityConfiguration(providerName, includeInMigrations, schema));
        }
    }
}
