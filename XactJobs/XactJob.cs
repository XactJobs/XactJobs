namespace XactJobs
{
    public class XactJob
    {
        public long Id { get; private set; }

        /// <summary>
        /// Job status
        /// </summary>
        public XactJobStatus Status { get; private set; }

        public DateTime CreatedAt { get; private set; }

        public DateTime ScheduledAt { get; private set; }

        public DateTime? UpdatedAt { get; private set; }

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

        public string? Queue { get; private set; }

        public int ErrorCount { get; private set; }

        public string? ErrorMessage { get; private set; }

        public string? ErrorStackTrace { get; private set; }

        public XactJob(long id,
                       DateTime createdAt,
                       DateTime scheduledAt,
                       string typeName,
                       string methodName,
                       string methodArgs,
                       string? queue = null,
                       DateTime? updatedAt = null,
                       int errorCount = 0,
                       string? lastErrorMessage = null,
                       string? lastErrorStackTrace = null)
        {
            Id = id;
            TypeName = typeName;
            MethodName = methodName;
            MethodArgs = methodArgs;
            Queue = queue;
            CreatedAt = createdAt;
            ScheduledAt = scheduledAt;
            UpdatedAt = updatedAt;
            ErrorCount = errorCount;
            ErrorMessage = lastErrorMessage;
            ErrorStackTrace = lastErrorStackTrace;
        }

        internal void MarkCompleted()
        {
            UpdatedAt = DateTime.Now;
            Status = XactJobStatus.Completed;
        }

        internal void MarkFailed(Exception ex)
        {
            UpdatedAt = DateTime.UtcNow;
            ErrorCount = ErrorCount + 1;
            ErrorMessage = ex.Message;
            ErrorStackTrace = ex.StackTrace;

            // TODO Add retry strategy

            Status = ErrorCount <= 10 ? XactJobStatus.Failed : XactJobStatus.Cancelled;
            
            if (Status == XactJobStatus.Failed)
            {
                ScheduledAt = DateTime.UtcNow.AddSeconds(10);
            }
        }
    }
}
