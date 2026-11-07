// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

namespace XactJobs
{
    /// <summary>
    /// What the worker does when a job calls <c>SaveChanges</c> on its scoped <c>TDbContext</c> outside a transaction.
    /// Those changes are committed right away, so if the job runs again, e.g. after its lease expired, they happen again.
    /// </summary>
    public enum XactJobSaveCheck
    {
        /// <summary>
        /// Logs a warning, once per job method per worker host. The default.
        /// </summary>
        Warn = 0,

        /// <summary>
        /// Throws an <see cref="InvalidOperationException"/> from <c>SaveChanges</c>, so nothing is saved, and the job fails and is retried.
        /// </summary>
        Throw = 1,

        /// <summary>
        /// Doesn't check.
        /// </summary>
        Ignore = 2,
    }
}
