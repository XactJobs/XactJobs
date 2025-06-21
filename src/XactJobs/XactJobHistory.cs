namespace XactJobs
{
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