namespace XactJobs
{
    public class XactJobBase
    {
        public Guid Id { get; protected set; }

        public DateTime CreatedAt
        {
            get
            {
                UUIDNext.Tools.UuidDecoder.TryDecodeTimestamp(Id, out var createdAt);
                return createdAt;
            }
        }


        /// <summary>
        /// Job status
        /// </summary>
        public XactJobStatus Status { get; protected set; }

        /// <summary>
        /// When should the job be executed
        /// </summary>
        public DateTime ScheduledAt { get; protected set; }

        /// <summary>
        /// Assembly qualified name of the declaring type
        /// </summary>
        public string TypeName { get; protected set; }

        /// <summary>
        /// Method name to be called
        /// </summary>
        public string MethodName { get; protected set; }

        /// <summary>
        /// Arguments to be passed to the method
        /// </summary>
        public string MethodArgs { get; protected set; }

        public string Queue { get; protected set; }

        public int ErrorCount { get; protected set; }

        public DateTime? ErrorTime { get; protected set; }

        public string? ErrorMessage { get; protected set; }

        public string? ErrorStackTrace { get; protected set; }

        public XactJobBase(Guid id,
                           DateTime scheduledAt,
                           XactJobStatus status,
                           string typeName,
                           string methodName,
                           string methodArgs,
                           string queue,
                           int errorCount = 0,
                           DateTime? errorTime = null,
                           string? errorMessage = null,
                           string? errorStackTrace = null)
        {
            Id = id;
            TypeName = typeName;
            MethodName = methodName;
            MethodArgs = methodArgs;
            Queue = queue;
            Status = status;
            ScheduledAt = scheduledAt;
            ErrorCount = errorCount;
            ErrorTime = errorTime;
            ErrorMessage = errorMessage;
            ErrorStackTrace = errorStackTrace;
        }
    }

    public class XactJob: XactJobBase
    {
        public DateTime? LeasedUntil { get; private set; }
        public Guid? Leaser { get; set; }

        public XactJob(Guid id,
                       DateTime scheduledAt,
                       XactJobStatus status,
                       string typeName,
                       string methodName,
                       string methodArgs,
                       string queue,
                       Guid? leaser = null,
                       DateTime? leasedUntil = null,
                       int errorCount = 0,
                       DateTime? errorTime = null,
                       string? errorMessage = null,
                       string? errorStackTrace = null)
            : base(id,
                   scheduledAt,
                   status,
                   typeName,
                   methodName,
                   methodArgs,
                   queue,
                   errorCount,
                   errorTime,
                   errorMessage,
                   errorStackTrace)
        {
            Leaser = leaser;
            LeasedUntil = leasedUntil;
        }

        internal void MarkCompleted()
        {
            Status = XactJobStatus.Completed;
        }

        internal void MarkFailed(Exception ex)
        {
            ErrorTime = DateTime.UtcNow;
            ErrorCount = ErrorCount + 1;
            ErrorMessage = ex.Message;
            ErrorStackTrace = ex.StackTrace;

            Leaser = null;
            LeasedUntil = null;

            // TODO Implement retry strategy

            Status = ErrorCount < 10 ? XactJobStatus.Failed : XactJobStatus.Cancelled;

            if (Status == XactJobStatus.Failed)
            {
                ScheduledAt = DateTime.UtcNow.AddSeconds(10);
            }
        }
    }

    public class XactJobArchive: XactJobBase
    {
        public DateTime CompletedAt { get; private set; }

        public XactJobArchive(Guid id,
                              DateTime scheduledAt,
                              XactJobStatus status,
                              DateTime completedAt,
                              string typeName,
                              string methodName,
                              string methodArgs,
                              string queue,
                              int errorCount = 0,
                              DateTime? errorTime = null,
                              string? errorMessage = null,
                              string? errorStackTrace = null)
            : base(id,
                   scheduledAt,
                   status,
                   typeName,
                   methodName,
                   methodArgs,
                   queue,
                   errorCount,
                   errorTime,
                   errorMessage,
                   errorStackTrace)
        {
            CompletedAt = completedAt;
        }

        public static XactJobArchive CreateFromJob(XactJob job, DateTime completedAt)
        {
            return new XactJobArchive(job.Id,
                                      job.ScheduledAt,
                                      job.Status,
                                      completedAt,
                                      job.TypeName,
                                      job.MethodName,
                                      job.MethodArgs,
                                      job.Queue,
                                      job.ErrorCount,
                                      job.ErrorTime,
                                      job.ErrorMessage,
                                      job.ErrorStackTrace);
        }
    }

} 