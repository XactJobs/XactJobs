namespace XactJobs
{
    public class XactJobPeriodic
    {
        /// <summary>
        /// Unique job name
        /// </summary>
        public string Id { get; private set; }

        /// <summary>
        /// When was the job first created
        /// </summary>
        public DateTime CreatedAt { get; private set; }

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
        /// Id of the latest executed job
        /// </summary>
        public Guid? LastJobId { get; private set; }


        public XactJobPeriodic(string id,
                               DateTime createdAt,
                               string cronExpression,
                               string typeName,
                               string methodName,
                               string methodArgs,
                               string queue,
                               Guid? lastJobId = null)
        {
            Id = id;
            CreatedAt = createdAt;
            CronExpression = cronExpression;
            TypeName = typeName;
            MethodName = methodName;
            MethodArgs = methodArgs;
            Queue = queue;
            LastJobId = lastJobId;
        }
    }
}
