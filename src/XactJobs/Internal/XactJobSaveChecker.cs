// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace XactJobs.Internal
{
    /// <summary>
    /// Checks the saves a job makes on the scoped DbContext the worker created for it, while the job runs (the worker's own save is after it).
    /// One per worker host, shared by its runners, so a job method is warned about once.
    /// </summary>
    internal sealed class XactJobSaveChecker
    {
        internal const string ReadMoreUrl = "https://github.com/XactJobs/XactJobs#exactly-once-database-effects";

        private readonly XactJobSaveCheck _mode;

        // the method name includes the parameter types (since 0.1.7), so overloads are told apart
        private readonly ConcurrentDictionary<(string TypeName, string MethodName), byte> _warned = new();

        public XactJobSaveChecker(XactJobSaveCheck mode)
        {
            _mode = mode;
        }

        /// <summary>
        /// Starts checking the job's saves, or returns null if there's nothing to check: Ignore, or Warn for a job method that was already warned about.
        /// </summary>
        public EventHandler<SavingChangesEventArgs>? Subscribe(DbContext dbContext, XactJob job, ILogger logger, string queue)
        {
            if (_mode == XactJobSaveCheck.Ignore) return null;

            if (_mode == XactJobSaveCheck.Warn && _warned.ContainsKey((job.TypeName, job.MethodName))) return null;

            var handler = CreateHandler(job, logger, queue);

            dbContext.SavingChanges += handler;

            return handler;
        }

        // A method of its own: a lambda in Subscribe would allocate its closure when Subscribe starts, also when it returns null
        private EventHandler<SavingChangesEventArgs> CreateHandler(XactJob job, ILogger logger, string queue)
        {
            return (sender, _) => OnSavingChanges((DbContext)sender!, job, logger, queue);
        }

        public static void Unsubscribe(DbContext dbContext, EventHandler<SavingChangesEventArgs>? handler)
        {
            if (handler != null)
            {
                dbContext.SavingChanges -= handler;
            }
        }

        private void OnSavingChanges(DbContext dbContext, XactJob job, ILogger logger, string queue)
        {
            // EF starts its own transaction for SaveChanges after this event, so null means the job didn't open one
            if (dbContext.Database.CurrentTransaction != null) return;

            if (_mode == XactJobSaveCheck.Throw)
            {
                throw new InvalidOperationException($"The job {job.TypeName}.{job.MethodName} called SaveChanges outside a transaction. " +
                    "Those changes would be committed right away, so if the job ran again they would happen again. Let the worker save them, " +
                    $"or save them in a transaction the job opens and doesn't commit. See {ReadMoreUrl}");
            }

            if (_warned.TryAdd((job.TypeName, job.MethodName), 0))
            {
                logger.LogWarning("{Queue}: The job {TypeName}.{MethodName} called SaveChanges outside a transaction. Those changes are committed right away, " +
                                  "so if the job runs again, e.g. after its lease expired, they happen again. Let the worker save them, or save them in a " +
                                  "transaction the job opens and doesn't commit. See {ReadMoreUrl} (logged once per job method)",
                                  queue, job.TypeName, job.MethodName, ReadMoreUrl);
            }
        }
    }
}
