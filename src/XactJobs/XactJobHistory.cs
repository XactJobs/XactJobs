// This file is part of XactJobs.
//
// XactJobs is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// XactJobs is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU Lesser General Public License for more details.
//
// You should have received a copy of the GNU Lesser General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.

namespace XactJobs
{
    public class XactJobHistory: XactJobBase
    {
        public DateTime ProcessedAt { get; init; }
        public XactJobStatus Status { get; init; }

        public string? ErrorMessage { get; init; }
        public string? ErrorStackTrace { get; init; }

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