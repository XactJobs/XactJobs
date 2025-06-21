namespace XactJobs
{
    public class XactJobBase
    {
        public long Id { get; init; }

        /// <summary>
        /// When should the job be executed
        /// </summary>
        public DateTime ScheduledAt { get; init; }

        /// <summary>
        /// Assembly qualified name of the declaring type
        /// </summary>
        public string TypeName { get; init; }

        /// <summary>
        /// Method name to be called
        /// </summary>
        public string MethodName { get; init; }

        /// <summary>
        /// Arguments to be passed to the method
        /// </summary>
        public string MethodArgs { get; init; }

        public string Queue { get; init; }

        public string? PeriodicJobId { get; init; }
        public string? CronExpression { get; init; }

        public int ErrorCount { get; init; }

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