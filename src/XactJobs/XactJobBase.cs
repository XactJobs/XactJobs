namespace XactJobs
{
    public class XactJobBase
    {
        public long Id { get; private set; }

        /// <summary>
        /// When should the job be executed
        /// </summary>
        public DateTime ScheduledAt { get; private set; }

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

        public string Queue { get; private set; }

        public string? PeriodicJobId { get; private set; }
        public string? CronExpression { get; private set; }

        public int ErrorCount { get; private set; }

        public XactJobBase(long id,
                           DateTime scheduledAt,
                           string typeName,
                           string methodName,
                           string methodArgs,
                           string queue,
                           string? periodicJobId = null,
                           string? cronExpression = null,
                           int errorCount = 0)
        {
            Id = id;
            ScheduledAt = scheduledAt;
            TypeName = typeName;
            MethodName = methodName;
            MethodArgs = methodArgs;
            Queue = queue;
            PeriodicJobId = periodicJobId;
            CronExpression = cronExpression;
            ErrorCount = errorCount;
        }
    }

} 