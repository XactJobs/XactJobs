using System.Linq.Expressions;

namespace XactJobs
{
    public class XactJobPeriodic
    {
        public Guid Id { get; private set; }

        /// <summary>
        /// Unique periodic job name
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// When was the job first created
        /// </summary>
        public DateTime CreatedAt { get; private set; }

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

        public XactJobPeriodic(Guid id,
                               string name,
                               DateTime createdAt,
                               DateTime updatedAt,
                               string cronExpression,
                               string typeName,
                               string methodName,
                               string methodArgs,
                               string queue,
                               bool isActive)
        {
            Id = id;
            Name = name;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            CronExpression = cronExpression;
            TypeName = typeName;
            MethodName = methodName;
            MethodArgs = methodArgs;
            Queue = queue;
            IsActive = isActive;
        }

        internal void UpdateDefinition(XactJobPeriodic job)
        {
            TypeName = job.TypeName;
            MethodName = job.MethodName;
            MethodArgs = job.MethodArgs;
            CronExpression = job.CronExpression;
            Queue = job.Queue;
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
                && Queue == job.Queue;
        }

        internal void Activate(bool isActive = true)
        {
            IsActive = isActive;
        }
    }
}
