// This file is part of XactJobs.
//
// XactJobs is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// XactJobs is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

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
        /// Version is increased every time the period job changes.
        /// </summary>
        public int Version { get; private set; }

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
            Version = Version + 1;
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
