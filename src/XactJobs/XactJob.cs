namespace XactJobs
{
    public class XactJobBase
    {
        public Guid Id { get; private set; }

        public DateTime CreatedAt
        {
            get
            {
                UUIDNext.Tools.UuidDecoder.TryDecodeTimestamp(Id, out var createdAt);
                return createdAt;
            }
        }

        /// <summary>
        /// When should the job be executed
        /// </summary>
        public DateTime ScheduledAt { get; protected set; }

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

        public Guid? PeriodicJobId { get; private set; }
        public string? CronExpression { get; private set; }

        public int ErrorCount { get; protected set; }

        public XactJobBase(Guid id,
                           DateTime scheduledAt,
                           string typeName,
                           string methodName,
                           string methodArgs,
                           string queue,
                           Guid? periodicJobId = null,
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

        public XactJob(Guid id,
                       DateTime scheduledAt,
                       string typeName,
                       string methodName,
                       string methodArgs,
                       string queue,
                       Guid? periodicJobId = null,
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

        internal void MarkFailed()
        {
            ErrorCount++;
        }
    }

    public class XactJobHistory: XactJobBase
    {
        public DateTime ProcessedAt { get; private set; }
        public XactJobStatus Status { get; private set; }

        public string? ErrorMessage { get; protected set; }
        public string? ErrorStackTrace { get; protected set; }

        public string? PeriodicJobName { get; private set; }

        public XactJobHistory(Guid id,
                              DateTime processedAt,
                              XactJobStatus status,
                              DateTime scheduledAt,
                              string typeName,
                              string methodName,
                              string methodArgs,
                              string queue,
                              Guid? periodicJobId = null,
                              int errorCount = 0,
                              string? periodicJobName = null,
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
            PeriodicJobName = periodicJobName;
            ErrorMessage = errorMessage;
            ErrorStackTrace = errorStackTrace;
        }

        public static XactJobHistory CreateFromJob(XactJob job,
                                                   XactJobPeriodic? periodicJob,
                                                   DateTime processedAt,
                                                   XactJobStatus status,
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
                                      job.ErrorCount,
                                      periodicJob?.Name,
                                      periodicJob?.CronExpression,
                                      innerMostEx?.Message,
                                      ex?.StackTrace);
        }
    }

} 