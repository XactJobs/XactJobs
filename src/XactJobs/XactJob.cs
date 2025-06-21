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

    public class XactJob: XactJobBase
    {
        public DateTime? LeasedUntil { get; private set; }
        public Guid? Leaser { get; set; }

        // needed for the FK
        public XactJobPeriodic? PeriodicJob { get; private set; }

        public XactJob(long id,
                       DateTime scheduledAt,
                       string typeName,
                       string methodName,
                       string methodArgs,
                       string queue,
                       string? periodicJobId = null,
                       string? cronExpression = null,
                       int errorCount = 0,
                       Guid? leaser = null,
                       DateTime? leasedUntil = null)
            : base(id,
                   scheduledAt,
                   typeName,
                   methodName,
                   methodArgs,
                   queue,
                   periodicJobId,
                   cronExpression,
                   errorCount)
        {
            Leaser = leaser;
            LeasedUntil = leasedUntil;
        }
    }

    public class XactJobHistory: XactJobBase
    {
        public DateTime ProcessedAt { get; private set; }
        public XactJobStatus Status { get; private set; }

        public string? ErrorMessage { get; protected set; }
        public string? ErrorStackTrace { get; protected set; }

        public XactJobHistory(long id,
                              DateTime processedAt,
                              XactJobStatus status,
                              DateTime scheduledAt,
                              string typeName,
                              string methodName,
                              string methodArgs,
                              string queue,
                              string? periodicJobId = null,
                              int errorCount = 0,
                              string? cronExpression = null,
                              string? errorMessage = null,
                              string? errorStackTrace = null)
            : base(id,
                   scheduledAt,
                   typeName,
                   methodName,
                   methodArgs,
                   queue,
                   periodicJobId,
                   cronExpression,
                   errorCount)
        {
            ProcessedAt = processedAt;
            Status = status;
            ErrorMessage = errorMessage;
            ErrorStackTrace = errorStackTrace;
        }

        public static XactJobHistory CreateFromJob(XactJob job,
                                                   XactJobPeriodic? periodicJob,
                                                   DateTime processedAt,
                                                   XactJobStatus status,
                                                   int errorCount,
                                                   Exception? ex)
        {
            var innerMostEx = ex;
            while (innerMostEx?.InnerException != null)
            {
                innerMostEx = innerMostEx.InnerException;
            }

            return new XactJobHistory(job.Id,
                                      processedAt,
                                      status,
                                      job.ScheduledAt,
                                      job.TypeName,
                                      job.MethodName,
                                      job.MethodArgs,
                                      job.Queue,
                                      job.PeriodicJobId,
                                      errorCount,
                                      periodicJob?.CronExpression,
                                      innerMostEx?.Message,
                                      ex?.StackTrace);
        }
    }

} 