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
        /// The time zone of the cron expression: an IANA id (e.g. Europe/Zagreb), or null for UTC. See <see cref="XactCronSchedule"/>.
        /// </summary>
        public string? TimeZone { get; private set; }

        /// <summary>
        /// The local times the job doesn't run at, or null: see <see cref="XactCronSchedule.Exclusions"/>. Not part of the definition:
        /// a change doesn't make a new version.
        /// </summary>
        public string? Exclusions { get; private set; }

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
                               int version,
                               string? timeZone = null,
                               string? exclusions = null)
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
            TimeZone = timeZone;
            Exclusions = exclusions;
        }

        internal void UpdateDefinition(XactJobPeriodic job)
        {
            UpdatedAt = DateTime.UtcNow;
            TypeName = job.TypeName;
            MethodName = job.MethodName;
            MethodArgs = job.MethodArgs;
            CronExpression = job.CronExpression;
            TimeZone = job.TimeZone;
            Exclusions = job.Exclusions;
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
                && TimeZone == job.TimeZone
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

        /// <summary>Changes the exclusions alone: the definition, and so the version, stays.</summary>
        internal void UpdateExclusions(string? exclusions)
        {
            UpdatedAt = DateTime.UtcNow;
            Exclusions = exclusions;
        }

        /// <summary>
        /// The run's time, in UTC, is one the exclusions leave out, in the schedule's time zone. Not on a node that doesn't know the zone:
        /// the run runs there, as it did without exclusions, and its next run is left to a node that knows it.
        /// </summary>
        internal bool Excludes(DateTime runAtUtc)
        {
            if (Exclusions == null) return false;

            TimeZoneInfo timeZone;

            try
            {
                timeZone = CronUtil.CronTimeZones.Find(TimeZone);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return false;
            }

            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(runAtUtc, DateTimeKind.Utc), timeZone);

            return CronUtil.CronExclusions.Parse(Exclusions)!.ResumeAfter(local) != null;
        }

        /// <summary>The exclusions as they're now in the table, read before scheduling the next run: not a change to save.</summary>
        internal void RefreshExclusions(string? exclusions)
        {
            Exclusions = exclusions;
        }

        internal void Activate(bool isActive = true)
        {
            IsActive = isActive;
        }
    }
}
