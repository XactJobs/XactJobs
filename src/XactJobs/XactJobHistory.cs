// Copyright (c) 2025 XactJobs
// Licensed under the MIT License. See LICENSE in the repository root.

namespace XactJobs
{
    public class XactJobHistory: XactJobBase
    {
        public DateTime ProcessedAt { get; init; }
        public XactJobStatus Status { get; init; }

        public string? ErrorMessage { get; init; }
        public string? ErrorStackTrace { get; init; }

        /// <summary>
        /// A run of a periodic job: the state it started with (<see cref="XactJobPeriodic.State"/>).
        /// </summary>
        public string? PreviousState { get; init; }

        /// <summary>
        /// A run of a periodic job: the state after it. The same as <see cref="PreviousState"/>, unless the run completed and
        /// set one.
        /// </summary>
        public string? State { get; init; }

        public XactJobHistory(long id,
                              DateTime processedAt,
                              XactJobStatus status,
                              DateTime scheduledAt,
                              string typeName,
                              string methodName,
                              string methodArgs,
                              string queue,
                              string? periodicJobId = null,
                              string? cronExpression = null,
                              int? periodicJobVersion = null,
                              int errorCount = 0,
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
                   periodicJobVersion,
                   errorCount)
        {
            ProcessedAt = processedAt;
            Status = status;
            ErrorMessage = errorMessage;
            ErrorStackTrace = errorStackTrace;
        }

        public static XactJobHistory CreateFromJob(XactJob job,
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

            return CreateFromJob(job, processedAt, status, errorCount, innerMostEx?.Message, ex?.StackTrace, job.ScheduledAt);
        }

        /// <summary>
        /// A continuation that waited for its batch has the reserved scheduled time, so the history gets the time it was claimed instead.
        /// </summary>
        internal static XactJobHistory CreateFromJob(XactJob job,
                                                     DateTime processedAt,
                                                     XactJobStatus status,
                                                     int errorCount,
                                                     string? errorMessage,
                                                     string? errorStackTrace,
                                                     DateTime scheduledAt,
                                                     string? previousState = null,
                                                     string? state = null)
        {
            return new XactJobHistory(job.Id,
                                      processedAt,
                                      status,
                                      scheduledAt,
                                      job.TypeName,
                                      job.MethodName,
                                      job.MethodArgs,
                                      job.Queue,
                                      job.PeriodicJobId,
                                      job.CronExpression,
                                      job.PeriodicJobVersion,
                                      errorCount,
                                      errorMessage,
                                      errorStackTrace)
            {
                BatchId = job.BatchId,
                AwaitsBatchId = job.AwaitsBatchId,
                ContinuationCondition = job.ContinuationCondition,

                // null when it's the same: e.g. the first attempt of a continuation, whose claim saved its claim time
                OriginallyScheduledAt = job.OriginallyScheduledAt != scheduledAt ? job.OriginallyScheduledAt : null,
                TraceParent = job.TraceParent,
                PreviousState = previousState,
                State = state
            };
        }
    }

} 