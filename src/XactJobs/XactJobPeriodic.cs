// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

using System.Security.Cryptography;

namespace XactJobs
{
    public class XactJobPeriodic
    {
        /// <summary>
        /// Unique periodic job name
        /// </summary>
        public string Id { get; init; }

        /// <summary>
        /// When was the job first created
        /// </summary>
        public DateTime CreatedAt { get; init; }

        /// <summary>
        /// When was the job last updated
        /// </summary>
        public DateTime UpdatedAt { get; private set; }

        /// <summary>
        /// Gets or sets the cron expression that defines the schedule for executing tasks.
        /// </summary>
        /// <remarks>The cron expression determines the frequency and timing of task execution. Ensure the
        /// expression is valid and adheres to the expected syntax to avoid scheduling errors.</remarks>
        public string CronExpression { get; private set; }

        /// <summary>
        /// Assembly qualified name of the declaring type
        /// </summary>
        public string TypeName { get; private set; }

        /// <summary>
        /// Method name to be called
        /// </summary>
        public string MethodName { get; private set; }

        /// <summary>
        /// Arguments to be passed to the method
        /// </summary>
        public string MethodArgs { get; private set; }

        /// <summary>
        /// Which queue to run the job in.
        /// </summary>
        public string Queue { get; private set; }

        /// <summary>
        /// Is the periodic job active
        /// </summary>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Identifies the definition of the periodic job. It's a new random value every time the periodic job is
        /// created or changed, so only the job rows of the last definition saved are compatible with it, even if several
        /// nodes change it at the same time. It says nothing about the order of the changes (see <see cref="UpdatedAt"/>).
        /// </summary>
        public int Version { get; private set; }

        /// <summary>
        /// The longest state a periodic job can keep, in characters.
        /// </summary>
        public const int MaxStateLength = 2000;

        /// <summary>
        /// The state the last completed run left for the next one (<see cref="XactJobContext.PeriodicState"/>), or null. It
        /// belongs to the periodic job's id: a change of its definition (<c>JobEnsurePeriodic</c>) keeps it.
        /// </summary>
        public string? State { get; internal set; }

        public XactJobPeriodic(string id,
                               DateTime createdAt,
                               DateTime updatedAt,
                               string cronExpression,
                               string typeName,
                               string methodName,
                               string methodArgs,
                               string queue,
                               bool isActive,
                               int version)
        {
            Id = id;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            CronExpression = cronExpression;
            TypeName = typeName;
            MethodName = methodName;
            MethodArgs = methodArgs;
            Queue = queue;
            IsActive = isActive;
            Version = version;
        }

        internal void UpdateDefinition(XactJobPeriodic job)
        {
            UpdatedAt = DateTime.UtcNow;
            TypeName = job.TypeName;
            MethodName = job.MethodName;
            MethodArgs = job.MethodArgs;
            CronExpression = job.CronExpression;
            Queue = job.Queue;

            var previousVersion = Version;

            do
            {
                Version = NewVersion();
            }
            while (Version == previousVersion);
        }

        /// <summary>
        /// Starts from 2, because job rows from before versions existed have no version, and count as version 1.
        /// </summary>
        internal static int NewVersion()
        {
            return RandomNumberGenerator.GetInt32(2, int.MaxValue);
        }

        internal bool IsCompatibleWith(XactJobPeriodic job)
        {
            return TypeName == job.TypeName 
                && MethodName == job.MethodName 
                && MethodArgs == job.MethodArgs 
                && CronExpression == job.CronExpression 
                && Queue == job.Queue;
        }

        internal bool IsCompatibleWith(XactJob job)
        {
            return TypeName == job.TypeName 
                && MethodName == job.MethodName 
                && MethodArgs == job.MethodArgs 
                && CronExpression == job.CronExpression 
                && Queue == job.Queue
                && Version == (job.PeriodicJobVersion ?? 1);
        }

        internal void Activate(bool isActive = true)
        {
            IsActive = isActive;
        }
    }
}
